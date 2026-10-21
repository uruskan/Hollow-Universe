using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Synthetic native presentation/lifetime evidence only. These diagnostic cohorts deliberately bypass the
    // production seal builder, and cannot authenticate an asset or qualify production placement/appearance.
    private async Task CheckFormationView()
    {
        var source = new FormationViewProbeSurface();
        using Process process = Process.GetCurrentProcess();
        var clock = Stopwatch.StartNew();
        long peak = 0;
        string phase = "setup";
        string? failure = null;
        bool completed = false;
        var observations = new List<object>();
        var resources = new List<IDisposable>();
        SubViewport? viewport = null;
        TerrainLod? terrain = null;
        FormationFieldView? view = null, capped = null;
        TerrainCohortTicket? shared = null;
        try
        {
            Budget();
            Shader shader = Own(new Shader { Code = """
                shader_type spatial;
                instance uniform vec2 morph_range = vec2(1e12, 2e12);
                instance uniform int tile_face = 0;
                void vertex() {
                    vec3 fine = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
                    float k = clamp((length(fine-CAMERA_POSITION_WORLD)-morph_range.x) / max(morph_range.y-morph_range.x,1.0),0.0,1.0);
                    VERTEX = mix(VERTEX,CUSTOM0.xyz,k);
                }
                void fragment() { ALBEDO = vec3(0.3,0.4,0.2); }
                """ });
            ShaderMaterial terrainMaterial = Own(new ShaderMaterial { Shader = shader });
            StandardMaterial3D visualMaterial = Own(new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.5f, 0.2f) });
            ArrayMesh mesh = Own(FormationViewProbeMesh(source.Definition.Geometry, visualMaterial));
            var template = new FormationVisualTemplate(source.Definition, source.Near, source.Far,
                new[] { new FormationVisualTemplate.Part(mesh, Transform3D.Identity) },
                new[] { new FormationVisualTemplate.Part(mesh, Transform3D.Identity) });
            viewport = new SubViewport { Name = "FormationViewNativeProbe", Size = new Vector2I(1, 1),
                OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
            AddChild(viewport);
            var terrainParent = new Node3D { Name = "FormationSupportOnlyAncestor" };
            viewport.AddChild(terrainParent);
            terrain = new TerrainLod { Name = "FormationSupportTerrain", Hurry = true };
            terrainParent.AddChild(terrain);
            var frame = new RenderFrame(source.FirstPiece.Transform.Position);
            TerrainViewLimits terrainLimits = CohortProbeLimits() with { MaxTickets = 2 };
            terrain.Setup(source, frame, terrainMaterial, null, terrainLimits);
            Vector3D distant = new(100_000, 0, 0);
            Vector3D firstCamera = source.FirstPiece.Transform.Position + new Vector3D(0, 0, 3);
            Vector3D replacementCamera = source.FirstPiece.Transform.Position + new Vector3D(80, 0, 3);
            var limits = new FormationViewLimits
            {
                MaxCohorts = 3, MaxPieces = 3, MaxMeshInstances = 6, MaxQueryNodes = 16,
                MaxQueryBytes = 16384, MaxEstimatedInstanceBytes = 65536,
                Reach = 30, NearDistance = 10, LodHysteresis = 1,
            };
            await UntilView(() => { terrain.UpdateLod(distant); return terrain.IsIdle; });

            phase = "cohort publication";
            source.Probe.Block = true; source.Probe.Entered.Reset(); source.Probe.Continue.Reset();
            view = new FormationFieldView { Name = "FormationViewSibling" }; viewport.AddChild(view);
            view.Setup(source.Formations, terrain, frame, new[] { template }, limits);
            view.PrepareDesired(firstCamera);
            view.PublishReady(firstCamera);
            Expect("formation view starts hidden before terrain admission is ready", view.Statistics.Cohorts == 1
                && view.Statistics.Pieces == 1 && view.Statistics.MeshInstances == 2 && view.Statistics.VisibleCohorts == 0
                && VisibleFormationMeshes(view) == 0 && terrain.CohortDiagnostics.Tickets == 1,
                "both borrowed near/far meshes have bounded native instances, but no visible cliff precedes its terrain cohort");
            terrain.UpdateLod(distant);
            await UntilView(() => source.Probe.Entered.IsSet);
            view.PublishReady(firstCamera);
            Expect("formation view cannot publish while required terrain work is blocked", VisibleFormationMeshes(view) == 0
                && view.Statistics.WaitingCohorts == 1 && terrain.DrawnTiles == 6,
                "actual terrain worker is paused; parent terrain coverage is insufficient for formation readiness");
            source.Probe.Block = false; source.Probe.Continue.Set();
            await UntilView(() => terrain.CohortDiagnostics.CompletedJobs == 1);
            view.PublishReady(firstCamera);
            Expect("formation view cannot publish completed but unuploaded terrain", VisibleFormationMeshes(view) == 0
                && terrain.CohortDiagnostics.UploadedNodes == 6,
                "completed worker output alone cannot make the formation visible");
            await UntilView(() =>
            {
                view.PrepareDesired(firstCamera); terrain.UpdateLod(distant); view.PublishReady(firstCamera);
                return view.Statistics.VisibleCohorts == 1;
            });
            Expect("formation view publishes exactly one qualified LOD after terrain becomes ready", VisibleFormationMeshes(view) == 1
                && view.Statistics.Pieces == 1 && view.Statistics.MeshInstances == 2 && view.Statistics.Failure == null,
                "the cohort root is published only after visible finest support; near/far native resources remain bounded");
            observations.Add(new { phase, statistics = view.Statistics, terrain = terrain.CohortDiagnostics });

            phase = "terrain effective visibility";
            foreach (Node3D hidden in new Node3D[] { terrain, terrainParent })
            {
                long revision = terrain.ContactRevision;
                hidden.Visible = false;
                view.PublishReady(firstCamera);
                Expect("formation view hides when " + hidden.Name + " is hidden", view.IsVisibleInTree()
                    && terrain.ContactRevision == revision && view.Statistics.VisibleCohorts == 0
                    && VisibleFormationMeshes(view) == 0 && terrain.CohortDiagnostics.Tickets == 1,
                    "the presentation sibling stays effectively visible, while terrain visibility removes cohort readiness without releasing its ticket");
                hidden.Visible = true;
                view.PublishReady(firstCamera);
                Expect("formation view recovers after " + hidden.Name + " is restored", view.Statistics.VisibleCohorts == 1
                    && VisibleFormationMeshes(view) == 1 && terrain.ContactRevision == revision,
                    "the same native support and ticket become ready again without rescheduling or a new terrain revision");
            }

            phase = "render origin rebase";
            MeshInstance3D visible = FormationViewNodes(view).OfType<MeshInstance3D>().Single(n => n.IsVisibleInTree());
            Transform3D before = visible.GlobalTransform;
            ulong visibleId = visible.GetInstanceId();
            Vector3 shift = frame.Move(frame.Origin + new Vector3D(7, -3, 2));
            terrain.Reanchor(); view.PublishReady(firstCamera);
            Transform3D after = visible.GlobalTransform;
            Vector3 localPoint = new(-0.5f, 0, 0);
            Vector3 expected = frame.ToRender(source.FirstPiece.Transform.Transform(new Vector3D(-0.5, 0, 0)));
            Expect("formation view rebase preserves body-space placement and native identity", visible.GetInstanceId() == visibleId
                && visible.IsVisibleInTree() && (after.Origin + shift - before.Origin).Length() <= 0.0001f
                && after.Basis == before.Basis && (after * localPoint - expected).Length() <= 0.0001f,
                "actual native mesh transform is checked against a physical piece vertex after the render origin moves");

            phase = "partial terrain admission rollback";
            TerrainCohortTicket marker = Acquire(source.First);
            ulong beforeTicket = marker.Id;
            terrain.ReleaseCohort(marker, () => { });
            ulong[] previousNodes = FormationViewNodes(view).Select(n => n.GetInstanceId()).ToArray();
            FormationFieldView.Diagnostics previous = view.Statistics;
            long terrainRefusals = terrain.CohortDiagnostics.RefusedAcquisitions;
            view.PrepareDesired(replacementCamera); view.PublishReady(replacementCamera);
            shared = Acquire(source.First);
            Expect("formation view rolls back partial terrain admission without retiring prior view",
                shared.Id == beforeTicket + 2 && terrain.CohortDiagnostics.RefusedAcquisitions == terrainRefusals + 1
                && terrain.CohortDiagnostics.Tickets == 2 && view.Statistics.Refusals == previous.Refusals + 1
                && view.Statistics.Cohorts == 1 && view.Statistics.Pieces == 1 && VisibleFormationMeshes(view) == 1
                && previousNodes.SequenceEqual(FormationViewNodes(view).Select(n => n.GetInstanceId())),
                "B obtains one temporary ticket, C exceeds the two-ticket terrain cap, B rolls back, and A retains every native node; the second remaining ticket is the explicit shared consumer");
            observations.Add(new { phase, statistics = view.Statistics, terrain = terrain.CohortDiagnostics });

            phase = "shared ticket and repeated stop";
            TerrainCohortDiagnostics sharedState = terrain.CohortDiagnostics;
            GodotObject[] owned = FormationViewNodes(view).Cast<GodotObject>().ToArray();
            view.Stop(); view.Stop();
            Expect("formation view Stop releases native nodes while a shared consumer keeps terrain ready",
                view.Statistics.Stopped && view.Statistics.Cohorts == 0 && view.Statistics.MeshInstances == 0
                && owned.All(n => !GodotObject.IsInstanceValid(n)) && terrain.CohortDiagnostics.Tickets == 1
                && terrain.CohortDiagnostics.ForcedNodes == sharedState.ForcedNodes
                && terrain.GetCohortState(shared) == TerrainCohortState.Ready,
                "repeated Stop is harmless; view-owned node retirement cannot release another consumer's support");
            terrain.ReleaseCohort(shared, () => { }); shared = null;
            Expect("formation view retirement preserves borrowed mesh and material", GodotObject.IsInstanceValid(mesh)
                && GodotObject.IsInstanceValid(visualMaterial) && mesh.GetSurfaceCount() == 1
                && mesh.SurfaceGetArrayLen(0) == 3 && mesh.SurfaceGetArrayIndexLen(0) == 3
                && mesh.SurfaceGetMaterial(0) == visualMaterial,
                "only native presentation instances are owned; admitted mesh/material resources remain with the caller");

            phase = "whole desired view-budget refusal";
            capped = new FormationFieldView { Name = "FormationViewCappedSibling" }; viewport.AddChild(capped);
            capped.Setup(source.Formations, terrain, frame, new[] { template }, limits with { MaxCohorts = 1 });
            capped.PrepareDesired(firstCamera); capped.PublishReady(firstCamera);
            if (capped.Statistics.VisibleCohorts != 1)
                throw new InvalidOperationException("Previously shared terrain did not remain ready for the capped view.");
            previousNodes = FormationViewNodes(capped).Select(n => n.GetInstanceId()).ToArray();
            previous = capped.Statistics;
            terrainRefusals = terrain.CohortDiagnostics.RefusedAcquisitions;
            capped.PrepareDesired(replacementCamera); capped.PublishReady(replacementCamera);
            Expect("formation view refuses the whole desired set before changing prior ownership",
                capped.Statistics.Refusals == previous.Refusals + 1 && capped.Statistics.Cohorts == 1
                && capped.Statistics.MeshInstances == 2 && capped.Statistics.Creations == previous.Creations
                && VisibleFormationMeshes(capped) == 1 && terrain.CohortDiagnostics.Tickets == 1
                && terrain.CohortDiagnostics.RefusedAcquisitions == terrainRefusals
                && previousNodes.SequenceEqual(FormationViewNodes(capped).Select(n => n.GetInstanceId())),
                "two desired cohorts exceed the one-cohort view allowance; no truncated prefix, terrain request or previous-node retirement occurs");
            capped.Stop(); capped.Stop();
            Expect("formation view final release returns every terrain ticket", terrain.CohortDiagnostics.Tickets == 0
                && terrain.CohortDiagnostics.ForcedNodes == 0 && capped.Statistics.Cohorts == 0,
                "both independently configured presentation owners retire their bounded reservations");
            completed = true;

            TerrainCohortTicket Acquire(FormationCohort cohort)
            {
                if (terrain.TryAcquireCohort(cohort, out TerrainCohortTicket? ticket) != TerrainCohortAcquireStatus.Accepted || ticket == null)
                    throw new InvalidOperationException("Formation view diagnostic shared ticket was unexpectedly refused.");
                return ticket;
            }
        }
        catch (Exception error)
        {
            failure = error.ToString(); Expect("formation view fixture execution", false, failure);
        }
        finally
        {
            source.Probe.Block = false; source.Probe.Continue.Set();
            capped?.Stop(); view?.Stop();
            if (shared != null) terrain?.ReleaseCohort(shared, () => { });
            Task workers = terrain?.WorkersStopped ?? Task.CompletedTask;
            terrain?.Stop(); viewport?.Free();
            for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
            await Task.WhenAny(workers, Task.Delay(TimeSpan.FromSeconds(5)));
            Expect("formation view fixture workers drain", workers.IsCompletedSuccessfully,
                "source synchronization handles outlive the actual terrain workers");
            if (workers.IsCompleted) source.Dispose();
            if (!string.IsNullOrEmpty(OutputPath)) File.WriteAllText(OutputPath + ".formation-view.json", JsonSerializer.Serialize(new
            {
                schema = "hollow.formation-view-native.v1", completed, phase, failure,
                seconds = clock.Elapsed.TotalSeconds, peakWorkingSet = peak,
                assetAuthenticated = false, placementQualified = false, appearanceApproved = false,
                gpuCullingMeasured = false, observations,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        T Own<T>(T value) where T : IDisposable { resources.Add(value); return value; }
        async Task UntilView(Func<bool> predicate)
        {
            for (int i = 0; i < 1024; i++)
            {
                Budget();
                if (predicate()) return;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            throw new InvalidOperationException("Formation view exceeded 1,024 native steps in " + phase);
        }
        void Budget()
        {
            process.Refresh(); peak = Math.Max(peak, process.WorkingSet64);
            if (clock.Elapsed.TotalSeconds > 35) throw new TimeoutException("Formation view native fixture exceeded 35 seconds.");
            if (process.WorkingSet64 > 1536L * 1024 * 1024)
                throw new InvalidOperationException("Formation view native fixture exceeded its 1536 MiB guard.");
        }
    }

    private static List<Node3D> FormationViewNodes(Node root)
    {
        var found = new List<Node3D>();
        Visit(root); return found;
        void Visit(Node parent)
        {
            for (int i = 0; i < parent.GetChildCount(); i++)
            {
                Node child = parent.GetChild(i);
                if (child is Node3D native) found.Add(native);
                Visit(child);
            }
        }
    }

    private static int VisibleFormationMeshes(Node root) =>
        FormationViewNodes(root).OfType<MeshInstance3D>().Count(mesh => mesh.IsVisibleInTree());

    private static ArrayMesh FormationViewProbeMesh(FormationGeometry geometry, Material material)
    {
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = geometry.Vertices.ToArray().Select(v => new Vector3((float)v.X, (float)v.Y, (float)v.Z)).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2 };
        var mesh = new ArrayMesh();
        try { mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); mesh.SurfaceSetMaterial(0, material); return mesh; }
        catch { mesh.Dispose(); throw; }
    }

    // Reuses the existing cohort probe's exact corner lattice and worker controls. Only the immutable diagnostic
    // field gains visible pieces; these synthetic pieces are not a substitute for production seal receipts.
    private sealed class FormationViewProbeSurface : ISurface, IGradedTerrain, ITerrainLatticeSurface,
        ITerrainPinnedSurface, IFormationSurface, IDisposable
    {
        internal readonly CohortProbeSurface Probe = new(120, '6');
        internal CliffSupportDefinition Definition { get; }
        internal CliffLodBinding Near { get; }
        internal CliffLodBinding Far { get; }
        internal FormationPiece FirstPiece { get; }
        internal FormationCohort First { get; }
        public double Radius => Probe.Radius;
        public double SeaRadius => Probe.SeaRadius;
        public bool IsFinalized => true;
        public TerrainLattice TerrainLattice => Probe.TerrainLattice;
        public TerrainPinSet TerrainPins { get; }
        public FormationField Formations { get; }
        internal FormationViewProbeSurface()
        {
            string digest = new('7', 64);
            var geometry = new FormationGeometry("formation-view-probe", 1, "fixture",
                new[] { new Vector3D(-0.5, 0, 0), new Vector3D(0.5, 0, 0), new Vector3D(0, 1, 0) },
                new[] { new GeometryTriangle(0, 1, 2, 0) }, new[] { new SupportPatch("face", "fixture", SupportCapabilities.None) },
                new FormationGeometryLimits { MaxVertices = 3, MaxTriangles = 1, MaxPatches = 1, MaxEstimatedBytes = 4096 });
            Near = new CliffLodBinding("near", digest, geometry, new[] { new CliffLodTriangleMap(0, 0) }, 0, 1);
            Far = new CliffLodBinding("far", digest, geometry, new[] { new CliffLodTriangleMap(0, 0) }, 0, 1);
            Definition = new CliffSupportDefinition(geometry, digest, digest, 1, 1,
                new[] { new CliffPatchDomain("face", new[] { new CliffPatchBoundary(new[] { 0, 1, 2 }) }) },
                Array.Empty<CliffContactDomain>(), new[] { Near, Far }, new CliffSupportLimits
                {
                    MaxDomains = 1, MaxBoundaryVertices = 3, MaxContacts = 0, MaxLods = 2, MaxMappedTriangles = 2,
                    MaxContactLoops = 0, MaxLoopVertices = 0,
                    MaxEstimatedResidentBytes = 32768, MaxEstimatedScratchBytes = 32768,
                });
            var pieces = new FormationPiece[3]; var cohorts = new FormationCohort[3];
            for (int i = 0; i < pieces.Length; i++)
            {
                var id = new FormationId((ulong)i + 1, 0);
                Vector3D position = Probe.Corner.Position + new Vector3D(i == 0 ? 0 : i == 1 ? 70 : 90, 0, 0);
                pieces[i] = new FormationPiece(new FormationPieceId(id, 0), "fixture", geometry,
                    new FormationTransform(position, Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 1));
                cohorts[i] = FixtureConstruct<FormationCohort>(id, new[] { pieces[i] },
                    Array.Empty<FormationContactSealReceipt>(), Array.Empty<TerrainLatticeTriangle>(),
                    new[] { Probe.Corner }, Probe.First.DependencyTiles.ToArray());
            }
            First = cohorts[0]; FirstPiece = pieces[0];
            var contract = new FormationFieldContract(digest, Radius, TerrainLattice.FinestTileLevel, FormationFieldAdmissionPolicy.IndependentPieces);
            Formations = FixtureConstruct<FormationField>(contract, digest, pieces, cohorts, Array.Empty<FormationFieldRejection>(),
                new FormationSupportIndex(pieces), default(FormationPlanDiagnostics), default(FormationFieldDiagnostics));
            TerrainPins = TerrainPinSet.Build(this, TerrainLattice, digest, Formations, new TerrainPinSetLimits
            {
                MaxVertices = 4, MaxInputPins = 4, MaxNormalTriangles = 32, MaxAdditionalSamples = 32,
                MaxEstimatedResidentBytes = 8192, MaxEstimatedScratchBytes = 32768,
            });
        }
        public double HeightAt(Vector3D direction) => Probe.HeightAt(direction);
        public double WaterLevelAt(Vector3D direction) => Probe.WaterLevelAt(direction);
        public GradedTerrainFields RawFieldsAt(Vector3D direction) => Probe.RawFieldsAt(direction);
        public ReadOnlySpan<FormationProtectedSite> ProtectedSites => ReadOnlySpan<FormationProtectedSite>.Empty;
        public void Dispose() => Probe.Dispose();
    }
}
