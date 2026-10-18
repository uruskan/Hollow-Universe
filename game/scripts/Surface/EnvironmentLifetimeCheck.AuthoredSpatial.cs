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
    // Root wires this isolated native state/lifetime probe. It exercises real augmented meshes, native batches,
    // atlas publications and the production admission path; it is not an appearance or GPU-culling benchmark.
    private async Task CheckAuthoredSpatial()
    {
        var source = new ProbeSurface(200);
        using Process process = Process.GetCurrentProcess();
        var clock = Stopwatch.StartNew();
        var observations = new List<object>();
        long peak = 0;
        string phase = "setup";
        bool completed = false;
        bool grassInitialized = false;
        string? failure = null;
        var resources = new List<IDisposable>();
        var originals = new List<MeshInstance3D>();
        SubViewport? viewport = null;
        GrassField? grass = null;
        TerrainLod? terrain = null;
        TerrainContactAtlas? retiredAtlas = null;
        SurfaceDiagnosticGlobals? globals = null;
        RenderFrame frame = RenderFrame.World;
        long planetOwner = PlanetGlobals.Claim(frame, source.Radius);
        try
        {
            Budget();
            ImageTexture zero = Own(GrassRenderSolid(new Color(0, 0, 0, 1)));
            ImageTexture one = Own(GrassRenderSolid(Colors.White));
            ImageTexture noise = Own(GrassRenderSolid(new Color(0.7f, 0.7f, 0.7f, 1)));
            ImageTexture3D dark = Own(GrassRenderVolume(Colors.Black));
            ImageTexture3D clear = Own(GrassRenderVolume(Colors.White));
            globals = new SurfaceDiagnosticGlobals();
            globals.Setup(source.Radius, zero, one, noise, dark, clear);
            viewport = new SubViewport { Name = "AuthoredSpatialNativeProbe", Size = new Vector2I(1, 1),
                OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
            AddChild(viewport);
            using var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-0.2f, 0, 0), new Vector3(0.2f, 0, 0), new Vector3(0, 0.6f, 0) };
            arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Back, Vector3.Back, Vector3.Back };
            arrays[(int)Mesh.ArrayType.Tangent] = new float[] { 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1 };
            arrays[(int)Mesh.ArrayType.TexUV] = new[] { Vector2.Zero, Vector2.Right, Vector2.Up };
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2 };
            ArrayMesh mesh = Own(new ArrayMesh());
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var descriptions = new AuthoredCoverRootMesh.Description[6];
            for (int i = 0; i < 6; i++)
            {
                var node = new MeshInstance3D { Name = $"fern_v{i / 2}_lod{i % 2}", Mesh = mesh };
                originals.Add(node); descriptions[i] = AuthoredCoverRootMesh.Describe(node);
            }
            GroundCoverRootMetadata roots = DiagnosticRoots(descriptions,
                new[] { Vector3D.Zero, Vector3D.Zero, Vector3D.Zero });
            ShaderMaterial coverMaterial = Own(new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_authored_cover.gdshader") });
            coverMaterial.SetShaderParameter("plant_height", 0.6f);
            coverMaterial.SetShaderParameter("albedo_texture", one);
            coverMaterial.SetShaderParameter("normal_texture", one);
            coverMaterial.SetShaderParameter("roughness_texture", one);
            var admissions = originals.Select((node, i) => new GrassField.AuthoredSpatialSource(3, i / 2, i % 2,
                node, roots.Get(node.Name.ToString()), coverMaterial, 22)).ToArray();
            Shader terrainShader = Own(new Shader { Code = """
                shader_type spatial;
                instance uniform vec2 morph_range = vec2(1e12, 2e12);
                instance uniform int tile_face = 0;
                void vertex() {
                    vec3 fine = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
                    float k = clamp((length(fine-CAMERA_POSITION_WORLD)-morph_range.x) / max(morph_range.y-morph_range.x,1.0),0.0,1.0);
                    VERTEX = mix(VERTEX,CUSTOM0.xyz,k);
                }
                void fragment() { ALBEDO = vec3(0.3); }
                """ });
            ShaderMaterial terrainMaterial = Own(new ShaderMaterial { Shader = terrainShader });
            var terrainParent = new Node3D { Name = "AuthoredSupportTerrainOnlyAncestor" };
            viewport.AddChild(terrainParent);
            terrain = new TerrainLod { Name = "AuthoredSupportTerrain", Hurry = true };
            terrainParent.AddChild(terrain);
            terrain.Setup(source, frame, terrainMaterial, null);
            // Grass is deliberately outside the terrain-only parent: its own tree visibility must remain true
            // when support disappears. This rules out ancestor hiding accidentally making the check pass.
            grass = new GrassField { Name = "AuthoredSupportSiblingGrass" }; viewport.AddChild(grass);
            grass.SetupAuthoredSpatialFixture(source, frame);
            grassInitialized = true;
            grass.ReplaceAuthoredSpatialFixturePlants(Points(32));
            grass.ConfigureAuthoredSpatial(admissions);
            DrawnTerrainTile contact = ContactAtlasTile(source, new TileKey(2, 0, 0, 0));
            var firstAtlas = new TerrainContactAtlas(0);
            grass.ReplaceAuthoredSpatialFixtureAtlas(firstAtlas);
            firstAtlas.UpdateFixture(new[] { contact }, 1);
            var snapshots = new List<DrawnTerrainTile>(TerrainContactAtlas.Capacity);
            bool copied = firstAtlas.TryCopyPublishedTiles(snapshots, out long copyVersion);
            Expect("authored spatial copies the actual completed atlas publication", copied && snapshots.Count == 1
                && ReferenceEquals(snapshots[0].Grid, contact.Grid) && copyVersion == firstAtlas.Version,
                "same published grid reference and native publication version, not newly requested TerrainLod state");

            phase = "continuously moving admission";
            Vector3D rest = new(0, source.Radius + 1.7, 0);
            await AdvanceUntil(s => s.Publications > 0, i => rest + new Vector3D((i & 1) == 0 ? -1 : 1, 0, 0));
            GrassField.AuthoredSpatialStatistics moving = grass.AuthoredSpatialDiagnostics;
            Expect("authored spatial moving requests cannot cancel upload forever", moving.Publications > 0
                && moving.CoalescedCameraUpdates > 0 && moving.Cancellations == 0,
                $"publications={moving.Publications}, coalesced={moving.CoalescedCameraUpdates}, cancellations={moving.Cancellations}, maximum age={moving.MaximumStageAgeSteps} admission steps; camera moves 2m each step");
            await AdvanceUntil(s => s.Ready && !s.LatestCameraPending, _ => rest);
            GrassField.AuthoredSpatialStatistics stable = grass.AuthoredSpatialDiagnostics;
            ulong[] ids = grass.AuthoredSpatialNativeOwnersForFixture().Select(o => o.GetInstanceId()).ToArray();
            for (int i = 0; i < 8; i++) grass.AdvanceAuthoredSpatial(terrain, rest, true);
            GrassField.AuthoredSpatialStatistics unchanged = grass.AuthoredSpatialDiagnostics;
            Expect("authored spatial unchanged selection creates no native resources or uploads", unchanged.Ready
                && unchanged.UploadedBytes == stable.UploadedBytes && unchanged.NativeBatchesCreated == stable.NativeBatchesCreated
                && ids.SequenceEqual(grass.AuthoredSpatialNativeOwnersForFixture().Select(o => o.GetInstanceId())),
                $"{stable.ResidentBatches} actual native batches / {stable.NearInstances + stable.MiddleInstances} whole plants; native object identities retained");
            Expect("authored spatial preserves admitted whole plants and reports LOD batches", stable.NearInstances + stable.MiddleInstances == 32
                && stable.FarInstances == 0 && stable.NearBatches + stable.MiddleBatches == stable.ResidentBatches
                && stable.ResidentBatches is > 0 and <= 512 && grass.VisibleAuthoredSpatialInstancesForFixture() == 32,
                "all 32 deterministic fixture plants survive; no extra density thinning, squeeze or inferred billboard root");
            observations.Add(new { phase, statistics = stable });

            phase = "atlas identity replacement";
            grass.ReplaceAuthoredSpatialFixturePlants(Points(32));
            await AdvanceUntil(s => s.StageAgeSteps > 0, _ => rest);
            long cancelled = grass.AuthoredSpatialDiagnostics.Cancellations;
            long published = grass.AuthoredSpatialDiagnostics.Publications;
            var replacement = new TerrainContactAtlas(0);
            replacement.UpdateFixture(new[] { contact }, 1);
            Expect("authored spatial replacement fixture has equal atlas version", replacement.Version == firstAtlas.Version,
                "distinct native atlas owners intentionally share the same numeric version");
            retiredAtlas = grass.ReplaceAuthoredSpatialFixtureAtlas(replacement);
            grass.AdvanceAuthoredSpatial(terrain, rest, true);
            Expect("authored spatial rejects stale atlas object even at equal version", grass.AuthoredSpatialDiagnostics.Cancellations == cancelled + 1
                && grass.AuthoredSpatialDiagnostics.Publications == published,
                "old staging is cancelled before native publication; new selection binds the replacement owner");
            retiredAtlas!.Dispose(); retiredAtlas = null;
            await AdvanceUntil(s => s.Ready, _ => rest);

            phase = "selection capacity refusal";
            int previousPlants = grass.AuthoredSpatialDiagnostics.NearInstances + grass.AuthoredSpatialDiagnostics.MiddleInstances;
            published = grass.AuthoredSpatialDiagnostics.Publications;
            grass.ReplaceAuthoredSpatialFixturePlants(Points(18001));
            await AdvanceUntil(s => s.Failure?.Contains("18,000", StringComparison.Ordinal) == true, _ => rest);
            GrassField.AuthoredSpatialStatistics overflow = grass.AuthoredSpatialDiagnostics;
            Expect("authored spatial instance overflow preserves the prior complete generation", overflow.Publications == published
                && overflow.NearInstances + overflow.MiddleInstances == previousPlants && !overflow.AdmissionHalted
                && grass.VisibleAuthoredSpatialInstancesForFixture() == previousPlants,
                $"18,001 supported selected plants refused before native upload; prior {previousPlants} plants retained; no truncated prefix");
            Expect("authored spatial admission examination stays bounded", overflow.ExaminedLastStep <= 4096
                && overflow.IndexedPlants == 18001 && overflow.IndexedTiles <= 512,
                $"last step={overflow.ExaminedLastStep}, indexed={overflow.IndexedPlants}, tiles={overflow.IndexedTiles}");
            observations.Add(new { phase, statistics = overflow });
            grass.ReplaceAuthoredSpatialFixturePlants(Points(32));
            await AdvanceUntil(s => s.Ready && s.Failure == null, _ => rest);

            phase = "missing support";
            replacement.UpdateFixture(Array.Empty<DrawnTerrainTile>(), 2);
            grass.AdvanceAuthoredSpatial(terrain, rest, true);
            Expect("authored spatial immediately hides groups lacking published support", grass.VisibleAuthoredSpatialInstancesForFixture() == 0,
                "no heightfield fallback and no partially grounded fragments; this is native visibility, not a raster proof");
            await AdvanceUntil(s => s.Ready && s.ResidentBatches == 0, _ => rest);
            Expect("authored spatial missing support admits zero whole plants", grass.AuthoredSpatialDiagnostics.NearInstances == 0
                && grass.AuthoredSpatialDiagnostics.MiddleInstances == 0,
                "empty but ready atlas publishes an empty authored generation");
            replacement.UpdateFixture(new[] { contact }, 3);
            await AdvanceUntil(s => s.Ready && s.NearInstances + s.MiddleInstances == 32, _ => rest);

            phase = "terrain and terrain-only ancestor visibility";
            Vector3D distant = new(0, 100_000, 0);
            await NativeUntil(() => { terrain.UpdateLod(distant); return terrain.IsIdle; });
            bool coarse = terrain.CopyContactTiles(distant, 200_000, snapshots);
            Expect("authored spatial visibility fixture uses actual drawn terrain", coarse && snapshots.Count == 6
                && snapshots.All(tile => tile.Key.Level == 0),
                "real TerrainLod worker/upload publication supplies six coarse roots; no synthetic snapshot for visibility checks");
            if (!coarse || snapshots.Count != 6 || snapshots.Any(tile => tile.Key.Level != 0))
                throw new InvalidOperationException("Authored support visibility fixture did not obtain all actual coarse roots.");
            foreach (Node3D hidden in new Node3D[] { terrain, terrainParent })
            {
                await NativeUntil(() =>
                {
                    replacement.Update(terrain, rest, 30);
                    grass.AdvanceAuthoredSpatial(terrain, rest, true);
                    return replacement.Ready && grass.AuthoredSpatialDiagnostics.Ready
                        && grass.VisibleAuthoredSpatialInstancesForFixture() == 32;
                });
                long visibleVersion = replacement.Version;
                long unchangedRevision = terrain.ContactRevision;
                hidden.Visible = false;
                replacement.Update(terrain, rest, 30);
                grass.AdvanceAuthoredSpatial(terrain, rest, true);
                Expect("authored spatial hides dependent plants when " + hidden.Name + " is hidden",
                    terrain.ContactRevision == unchangedRevision && replacement.Version != visibleVersion
                    && !replacement.Ready && replacement.ResidentTiles == 0 && grass.IsVisibleInTree()
                    && !grass.AuthoredSpatialDiagnostics.Ready && grass.VisibleAuthoredSpatialInstancesForFixture() == 0,
                    "same update removes support and hides every dependent batch; sibling grass remains effectively visible and the terrain-grid revision did not change");
                hidden.Visible = true;
                await NativeUntil(() =>
                {
                    replacement.Update(terrain, rest, 30);
                    grass.AdvanceAuthoredSpatial(terrain, rest, true);
                    return replacement.Ready && grass.AuthoredSpatialDiagnostics.Ready
                        && grass.VisibleAuthoredSpatialInstancesForFixture() == 32;
                });
                Expect("authored spatial recovers plants after " + hidden.Name + " is restored",
                    terrain.ContactRevision == unchangedRevision && replacement.Failure == null && grass.IsVisibleInTree()
                    && grass.AuthoredSpatialDiagnostics.NearInstances + grass.AuthoredSpatialDiagnostics.MiddleInstances == 32,
                    "the same actual terrain grids republish through normal bounded admission; no new terrain revision or fallback height sample is needed");
                observations.Add(new { phase, hidden = hidden.Name.ToString(), atlasVersion = replacement.Version,
                    terrainRevision = terrain.ContactRevision, statistics = grass.AuthoredSpatialDiagnostics });
            }

            phase = "construction and retirement quarantine";
            grass.ReplaceAuthoredSpatialFixturePlants(Points(32));
            grass.FaultAuthoredSpatialFixture(afterCreation: true, nodeRetirementFailures: 8, meshRetirementFailures: 8);
            await AdvanceUntil(s => s.AdmissionHalted, _ => rest);
            GodotObject[] faultOwners = grass.AuthoredSpatialNativeOwnersForFixture();
            GrassField.AuthoredSpatialStatistics faulted = grass.AuthoredSpatialDiagnostics;
            Expect("authored spatial failed cleanup retains native owners and reservations", faulted.AdmissionHalted
                && faulted.RetiringBatches > 0 && faulted.RetiringInstances > 0 && grass.VisibleAuthoredSpatialInstancesForFixture() == 0
                && faultOwners.Any(GodotObject.IsInstanceValid),
                $"retiring batches={faulted.RetiringBatches}, instance reservations={faulted.RetiringInstances}; all groups hidden");
            for (int i = 0; i < 4; i++) grass.AdvanceAuthoredSpatial(terrain, rest + new Vector3D(i, 0, 0), true);
            bool reconfigureRefused = false;
            try { grass.ConfigureAuthoredSpatial(admissions); } catch (InvalidOperationException) { reconfigureRefused = true; }
            Expect("authored spatial cleanup fault closes all later admission", reconfigureRefused
                && grass.AuthoredSpatialDiagnostics.NativeBatchesCreated == faulted.NativeBatchesCreated,
                "new camera requests and Configure cannot allocate behind the failed-retirement quarantine");
            grass.StopAuthoredSpatial();
            int retained = grass.AuthoredSpatialDiagnostics.RetiringBatches;
            grass.StopAuthoredSpatial();
            Expect("authored spatial repeated failed Stop retains the same bounded native set", grass.AuthoredSpatialDiagnostics.Configured
                && grass.AuthoredSpatialDiagnostics.AdmissionHalted && retained > 0
                && grass.AuthoredSpatialDiagnostics.RetiringBatches <= retained,
                "the same resident/staged owners are retried; accounting is not cleared when native retirement refuses");
            grass.FaultAuthoredSpatialFixture();
            grass.StopAuthoredSpatial();
            Expect("authored spatial recovered Stop releases every captured native owner", !grass.AuthoredSpatialDiagnostics.Configured
                && faultOwners.All(o => !GodotObject.IsInstanceValid(o)),
                "actual meshes, materials, instances and MultiMeshes released after clearing only diagnostic fault injection");
            Expect("authored spatial retirement does not dispose shared source art", GodotObject.IsInstanceValid(mesh)
                && GodotObject.IsInstanceValid(coverMaterial) && originals.Select(AuthoredCoverRootMesh.Describe).SequenceEqual(descriptions),
                "original mesh streams and borrowed materials survive owned augmentation/batch teardown");
            completed = true;

            async Task AdvanceUntil(Func<GrassField.AuthoredSpatialStatistics, bool> predicate, Func<int, Vector3D> camera)
            {
                for (int i = 0; i < 1024; i++)
                {
                    Budget();
                    grass.AdvanceAuthoredSpatial(terrain, camera(i), true);
                    if (predicate(grass.AuthoredSpatialDiagnostics)) return;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                throw new InvalidOperationException("Authored spatial fixture exceeded 1,024 admission steps in " + phase);
            }

            async Task NativeUntil(Func<bool> predicate)
            {
                for (int i = 0; i < 1024; i++)
                {
                    Budget();
                    if (predicate()) return;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                throw new InvalidOperationException("Authored spatial fixture exceeded 1,024 publication steps in " + phase);
            }
        }
        catch (Exception error)
        {
            failure = error.ToString();
            Expect("authored spatial fixture execution", false, failure);
        }
        finally
        {
            if (grassInitialized && grass != null)
            {
                grass.FaultAuthoredSpatialFixture();
                grass.StopAuthoredSpatial(); grass.Stop();
            }
            source.Continue.Set();
            Task terrainWorkers = terrain?.WorkersStopped ?? Task.CompletedTask;
            terrain?.Stop();
            viewport?.Free();
            retiredAtlas?.Dispose();
            globals?.Dispose();
            for (int i = originals.Count - 1; i >= 0; i--) originals[i].Free();
            for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
            PlanetGlobals.Clear(planetOwner);
            await Task.WhenAny(terrainWorkers, Task.Delay(TimeSpan.FromSeconds(5)));
            Expect("authored spatial fixture terrain workers drain", terrainWorkers.IsCompletedSuccessfully,
                "actual support workers finish before the probe surface synchronization handles are released");
            if (terrainWorkers.IsCompleted) source.Dispose();
            if (!string.IsNullOrEmpty(OutputPath)) File.WriteAllText(OutputPath + ".authored-spatial.json", JsonSerializer.Serialize(new
            {
                schema = "hollow.authored-spatial-native.v1", completed, phase, failure,
                seconds = clock.Elapsed.TotalSeconds, peakWorkingSet = peak,
                appearanceApproved = false, gpuCullingMeasured = false, observations
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        T Own<T>(T value) where T : IDisposable { resources.Add(value); return value; }
        Vector3D[] Points(int count)
        {
            const int cells = 1 << 12;
            int side = (int)Math.Ceiling(Math.Sqrt(count));
            var positions = new Vector3D[count];
            for (int i = 0; i < count; i++)
            {
                double x = cells / 2 - side / 2 + i % side + 0.37;
                double y = cells / 2 - side / 2 + i / side + 0.63;
                positions[i] = CubeSphere.Direction(2, 2 * x / cells - 1, 2 * y / cells - 1) * (source.Radius - 0.025);
            }
            return positions;
        }
        void Budget()
        {
            process.Refresh(); peak = Math.Max(peak, process.WorkingSet64);
            if (clock.Elapsed.TotalSeconds > 35) throw new TimeoutException("Authored spatial native fixture exceeded 35 seconds.");
            if (process.WorkingSet64 > 1536L * 1024 * 1024) throw new InvalidOperationException("Authored spatial native fixture exceeded its 1536 MiB guard.");
        }
    }
}
