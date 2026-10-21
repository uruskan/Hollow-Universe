using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Deliberate diagnostic mesh/image readback is confined to this isolated fixture. The production loader never
    // reads back, imports, constructs a world, approves soil colors, or activates a formation recipe.
    private FormationVisualFixtureState? _formationVisualFixture;
    private const long FormationVisualMiB = 1048576;

    private sealed class FormationVisualFixtureState
    {
        internal readonly List<Node> Nodes = new();
        internal readonly List<IDisposable> Dependencies = new();
        internal readonly List<FormationVisualSession> Sessions = new();
        internal readonly List<FormationVisualSession.Request> Requests = new();
        internal readonly List<FormationVisualLoader.UploadJob> Jobs = new();
        internal readonly List<GodotObject> NativeOwners = new();
        internal readonly TaskCompletionSource<bool> Workers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal T Node<T>(T node) where T : Node { Nodes.Add(node); return node; }
        internal T Own<T>(T owner) where T : IDisposable { Dependencies.Add(owner); return owner; }
    }

    // Coordinator dispatch: await CheckFormationVisual(); WriteReport("native-formation-visual").
    private async Task CheckFormationVisual()
    {
        if (_formationVisualFixture != null) throw new InvalidOperationException("Formation visual fixture still owns resources.");
        var state = new FormationVisualFixtureState();
        _formationVisualFixture = state;
        var clock = Stopwatch.StartNew();
        using Process process = Process.GetCurrentProcess();
        long peak = 0;
        string output = Path.GetFullPath(OutputPath);
        string directory = output + ".formation-visual-inputs";
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException("Formation diagnostic input directory must be new: " + directory);
        Directory.CreateDirectory(directory);
        var visualLimits = new FormationVisualLimits
        {
            MaxLods = 2, MaxFiles = 16, MaxPrimitives = 4, MaxMaterials = 4, MaxImages = 2,
            MaxVertices = 12, MaxIndices = 12, MaxJsonBytes = 65536, MaxFileBytes = 65536,
            MaxEncodedBytes = 262144, MaxStreamBytes = 4096, MaxDecodedImageBytes = 1024,
            MaxMetadataBytes = 2 * FormationVisualMiB, MaxReservationBytes = 32 * FormationVisualMiB,
            MaxImageDimension = 2, MaxImageHeaderBytes = 256,
        };
        var nativeLimits = new FormationNativeLimits
        {
            MaxTextures = 2, MaxMaterials = 4, MaxSurfaces = 4,
            MaxNativeBytes = 32 * FormationVisualMiB, MaxUploadBytes = 8 * FormationVisualMiB,
        };
        long entry = visualLimits.MaxReservationBytes + nativeLimits.MaxNativeBytes + nativeLimits.MaxUploadBytes;
        var budget = new FormationVisualSessionBudget(2, 3, entry + 2 * 4096 + 3 * 1024,
            visualLimits.MaxReservationBytes, nativeLimits.MaxUploadBytes, nativeLimits.MaxNativeBytes, 4096, 1024);
        try
        {
            Budget();
            var fixture = new FormationVisualTinyPackage(Path.Combine(directory, "accepted"));
            FormationPackage package = fixture.Load();
            FormationVisualData reference = FormationVisualDataLoader.Load(FormationVisualDataLoader.Preflight(
                package, fixture.Output, fixture.ReceiptPin, visualLimits));
            var approvals = new[]
            {
                new FormationMaterialApproval("native-fixture", 1, "near", 0), new FormationMaterialApproval("native-fixture", 1, "near", 1),
                new FormationMaterialApproval("native-fixture", 1, "far", 0), new FormationMaterialApproval("native-fixture", 1, "far", 1),
            };
            var selections = new[] { new FormationVisualLodSelection("native-fixture", 1, "near", "far") };
            // Fixture-authored tangent float4s happen to be valid 1/0 masks. Borrow their already authenticated,
            // immutable wrapper to exercise custom0 without a backdoor array constructor or mutable storage.
            string customIdentity = FormationVisualHash(Encoding.UTF8.GetBytes("isolated known custom0 fixture v1"));
            var custom = new FormationVisualCustom0Profile(reference.Lods.SelectMany(lod => lod.Primitives.Select(primitive =>
                new FormationVisualCustom0Binding(lod.GeometryKey, lod.GeometryRevision, lod.LodKey, primitive.Ordinal,
                    customIdentity, FormationVisualFloatHash(primitive.Tangents), primitive.Tangents))).ToArray());
            var profile = new FormationMaterialProfile(fixture.ReceiptPin, approvals, selections, custom);
            string profilePin = profile.Identity; // independently constructed fixture policy, never read from package metadata
            FormationVisualLoader.NativePlan plan = FormationVisualLoader.Preflight(package, reference, profile, nativeLimits);
            Expect("formation fixture authenticates a complete qualified package", package.Catalog.Qualified.Length == 1 &&
                package.Catalog.Recipes.Length == 0 && package.Catalog.Qualified[0].Geometry.TriangleCount == 2,
                "public package loader verifies fixture source, manifest, receipt, physical domain and both exact triangle mappings; no runtime art approval");
            Expect("formation native image roles reserve distinct mip variants", reference.Images.Count == 1 &&
                plan.Textures.Length == 2 && plan.TextureBytes == 40 && plan.Materials.Length == 4 && plan.Custom0Bytes == 192,
                "one 2x2 RGBA image has 20 bytes including mips; normal-renormalized and ordinary roles require two native textures; custom0 is four ordered float4 streams");
            Expect("formation fixture declares construction and diagnostic reservations", true,
                $"session={budget.MaxReservedBytes}; additional fixture/package/reference/readback allowance={16 * FormationVisualMiB}; allowances are not measured heap/VRAM bounds");

            var rejectedProfile = new FormationMaterialProfile(fixture.ReceiptPin, approvals[..3], selections);
            FormationVisualReject("formation missing explicit primitive approval is refused before upload",
                () => FormationVisualLoader.Preflight(package, reference, rejectedProfile, nativeLimits));
            FormationVisualReject("formation native texture variants are capped before upload",
                () => FormationVisualLoader.Preflight(package, reference, profile, nativeLimits with { MaxTextures = 1 }));
            FormationVisualReject("formation native bytes are capped before upload",
                () => FormationVisualLoader.Preflight(package, reference, profile, nativeLimits with { MaxNativeBytes = plan.NativeReservationBytes - 1 }));
            FormationVisualReject("formation custom0 bytes are capped before upload",
                () => FormationVisualLoader.Preflight(package, reference, profile, nativeLimits with { MaxCustom0Bytes = 191 }));
            var badMasks = new FormationVisualCustom0Profile(custom.Bindings.Select(binding =>
                binding with { ContentSha256 = new string('0', 64) }).ToArray());
            FormationVisualReject("formation custom0 digest is checked before upload", () => FormationVisualLoader.Preflight(
                package, reference, new FormationMaterialProfile(fixture.ReceiptPin, approvals, selections, badMasks), nativeLimits));
            var foreignMasks = new FormationVisualCustom0Profile(custom.Bindings, new string('0', 64));
            FormationVisualReject("formation custom0 cannot attach to a different visual receipt", () =>
                new FormationMaterialProfile(fixture.ReceiptPin, approvals, selections, foreignMasks));

            var unsupported = new FormationVisualTinyPackage(Path.Combine(directory, "unsupported-sampler"), nearest: true);
            FormationPackage unsupportedPackage = unsupported.Load();
            FormationVisualData unsupportedData = FormationVisualDataLoader.Load(FormationVisualDataLoader.Preflight(
                unsupportedPackage, unsupported.Output, unsupported.ReceiptPin, visualLimits));
            FormationVisualReject("formation valid glTF sampler unsupported by native profile is refused",
                () => FormationVisualLoader.Preflight(unsupportedPackage, unsupportedData,
                    new FormationMaterialProfile(unsupported.ReceiptPin, approvals, selections), nativeLimits));

            FormationVisualLoader.UploadJob cancelled = FormationVisualLoader.BeginUpload(plan);
            state.Jobs.Add(cancelled);
            cancelled.Pump(1);
            Expect("formation incremental upload stops after one owned texture", !cancelled.IsFinished && cancelled.OwnedResourceCount == 1,
                "first bounded step creates one private texture and releases its temporary Image; no mesh/material/template has published");
            cancelled.Cancel(); cancelled.Pump(1);
            Expect("formation partial upload cancellation publishes no owner", cancelled.Failure is OperationCanceledException &&
                !cancelled.IsCompleted && cancelled.OwnedResourceCount == 1,
                "cancelled partial texture stays owned until explicit cleanup");
            bool cancelClean = cancelled.TryDispose(out Exception? cancelError);
            Expect("formation cancelled partial private resources drain", cancelClean && cancelled.OwnedResourceCount == 0,
                cancelError?.ToString() ?? "no consumer or completed owner was exposed");
            CheckFormationVisualCleanupLedger(state);

            var rejectedBudget = new FormationVisualSessionBudget(2, 3, entry, visualLimits.MaxReservationBytes,
                nativeLimits.MaxUploadBytes, nativeLimits.MaxNativeBytes, 4096, 1024);
            var capped = new FormationVisualSession(package, fixture.Output, fixture.ReceiptPin, profile, profilePin,
                visualLimits, nativeLimits, rejectedBudget);
            state.Sessions.Add(capped);
            Node3D cappedRoot = state.Node(new Node3D()); AddChild(cappedRoot);
            FormationVisualReject("formation session reserves bindings before dispatch", () => capped.Acquire(cappedRoot));
            Expect("formation rejected admission launches no CPU work", capped.CaptureDiagnostics().TotalBytes == 0 &&
                capped.CaptureDiagnostics().LoadStarts == 0, "entry allowance alone cannot admit even its first root/binding");
            FormationVisualReject("formation session requires independent profile identity", () =>
            {
                using var wrong = new FormationVisualSession(package, fixture.Output, fixture.ReceiptPin, profile,
                    new string('f', 64), visualLimits, nativeLimits, budget);
            });
            capped.Stop(); capped.Pump(); cappedRoot.Free();

            var session = new FormationVisualSession(package, fixture.Output, fixture.ReceiptPin, profile, profilePin,
                visualLimits, nativeLimits, budget);
            state.Sessions.Add(session);
            Node3D leftRoot = state.Node(new Node3D { Name = "FormationVisualNearConsumer" }); AddChild(leftRoot);
            Node3D rightRoot = state.Node(new Node3D { Name = "FormationVisualFarConsumer" }); AddChild(rightRoot);
            FormationVisualSession.Request leftRequest = session.Acquire(leftRoot), rightRequest = session.Acquire(rightRoot);
            state.Requests.Add(leftRequest); state.Requests.Add(rightRequest);
            FormationVisualSession.Snapshot pending = session.CaptureDiagnostics();
            Expect("formation two leases reserve once before CPU or upload publication", pending.LoadStarts == 1 &&
                pending.UploadStarts == 0 && pending.CpuBytes == visualLimits.MaxReservationBytes &&
                pending.UploadBytes == nativeLimits.MaxUploadBytes && pending.NativeBytes == nativeLimits.MaxNativeBytes &&
                pending.Acquisitions == 2 && pending.ConsumerRoots == 2 && pending.TotalBytes == entry + 10240,
                "one entry plus two bindings and roots is charged immediately");
            await Wait(() => { session.Pump(); return leftRequest.Ready.IsCompleted && rightRequest.Ready.IsCompleted; }, "shared native upload");
            FormationVisualSession.Lease left = await leftRequest.Ready, right = await rightRequest.Ready;
            FormationVisualSession.Snapshot ready = session.CaptureDiagnostics();
            Expect("formation shared session performs one CPU load and one native upload", ready.Ready && ready.LoadStarts == 1 &&
                ready.UploadStarts == 1 && ready.CpuBytes == 0 && ready.UploadBytes == 0 &&
                ReferenceEquals(left.Templates, right.Templates) && left.Templates.Count == 1,
                "completed uploader releases authenticated CPU data/scratch before exposing shared immutable templates");
            Expect("formation surface override addresses remain immutable and complete", left.SurfaceSlots.Count == 4 &&
                left.SurfaceSlots is ICollection<FormationVisualSurfaceSlot> slots && slots.IsReadOnly &&
                left.SurfaceSlots.All(slot => slot.GeometryKey == "native-fixture" && slot.GeometryRevision == 1 &&
                    slot.PartIndex == 0 && slot.PrimitiveOrdinal == slot.SurfaceIndex),
                "per-piece overrides receive explicit LOD/primitive/surface addresses without changing shared materials");
            CheckFormationVisualStreamsAndMaterials(state, reference, left);
            await CaptureFormationVisual(state, leftRoot, rightRoot, left, right, output);

            Node3D extra = state.Node(new Node3D()); AddChild(extra); left.AddConsumer(extra);
            Expect("formation additional consumer root stays separately charged", session.CaptureDiagnostics().ConsumerRoots == 3 &&
                session.CaptureDiagnostics().TotalBytes == nativeLimits.MaxNativeBytes + 2 * 4096 + 3 * 1024,
                "CPU/upload allowances have retired; native and every lease/root remain charged");
            FormationVisualReject("formation consumer root cap applies across leases", () => right.AddConsumer(cappedRoot = state.Node(new Node3D())));
            if (GodotObject.IsInstanceValid(cappedRoot)) cappedRoot.Free();
            Task leftRetired = left.Retire(state.Workers.Task), rightRetired = right.Retire(Task.CompletedTask);
            session.Stop(); session.Pump();
            Expect("formation stop cannot retire live native consumers", !session.Stopped.IsCompleted && !leftRetired.IsCompleted &&
                !rightRetired.IsCompleted && session.CaptureDiagnostics().Acquisitions == 2,
                "Stop revokes new admissions but preserves published leases and their resources");
            leftRoot.Free(); rightRoot.Free(); extra.Free(); session.Pump();
            Expect("formation worker barrier retains last lease after all roots are invalid", rightRetired.IsCompletedSuccessfully &&
                !leftRetired.IsCompleted && !session.Stopped.IsCompleted && session.CaptureDiagnostics().Acquisitions == 1 &&
                session.CaptureDiagnostics().TotalBytes == nativeLimits.MaxNativeBytes + 4096 + 2 * 1024 &&
                state.NativeOwners.All(GodotObject.IsInstanceValid),
                "the two-root left lease stays charged until its independent worker completion task finishes");
            state.Workers.TrySetResult(true); session.Pump();
            Expect("formation completed workers and invalid roots permit final retirement", leftRetired.IsCompletedSuccessfully &&
                session.Stopped.IsCompletedSuccessfully && session.CaptureDiagnostics().TotalBytes == 0 &&
                state.NativeOwners.All(owner => !GodotObject.IsInstanceValid(owner)),
                "all captured meshes, materials and both native image variants are invalid only after the combined barrier");
            await session.Stopped;
            Expect("formation fixture stays inactive", package.Catalog.Recipes.Length == 0, "synthetic evidence cannot approve actual tangent repair, soil binding or gameplay placement");
        }
        finally
        {
            bool clean = true;
            state.Workers.TrySetResult(true);
            foreach (FormationVisualSession.Request request in state.Requests)
            {
                try
                {
                    if (request.Ready.IsCompletedSuccessfully) _ = request.Ready.Result.Retire(Task.CompletedTask);
                    else request.Cancel();
                }
                catch (Exception error) { clean = false; Expect("formation diagnostic lease cleanup", false, error.ToString()); }
            }
            foreach (FormationVisualSession session in state.Sessions) session.Stop();
            for (int i = state.Nodes.Count - 1; i >= 0; i--)
                try { if (GodotObject.IsInstanceValid(state.Nodes[i])) state.Nodes[i].Free(); }
                catch (Exception error) { clean = false; Expect("formation diagnostic native-root cleanup", false, error.ToString()); }
            var cleanupClock = Stopwatch.StartNew();
            var retried = new HashSet<FormationVisualSession>();
            while (state.Sessions.Any(session => !session.Stopped.IsCompleted) && cleanupClock.Elapsed.TotalSeconds < 5)
            {
                foreach (FormationVisualSession session in state.Sessions)
                {
                    try
                    {
                        session.Pump();
                        if (session.CaptureDiagnostics().Quarantined && retried.Add(session)) session.RetryCleanup();
                    }
                    catch (Exception error) { clean = false; Expect("formation diagnostic session cleanup", false, error.ToString()); }
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            clean &= state.Sessions.All(session => session.Stopped.IsCompleted && session.CaptureDiagnostics().TotalBytes == 0);
            foreach (FormationVisualSession session in state.Sessions) if (session.Stopped.IsFaulted) _ = session.Stopped.Exception;
            foreach (FormationVisualLoader.UploadJob job in state.Jobs)
                try
                {
                    if (!job.TryDispose(out Exception? error)) { clean = false; Expect("formation diagnostic upload cleanup", false, error!.ToString()); }
                }
                catch (Exception error) { clean = false; Expect("formation diagnostic upload cleanup", false, error.ToString()); }
            if (clean)
            {
                for (int i = state.Dependencies.Count - 1; i >= 0; i--)
                    try { state.Dependencies[i].Dispose(); }
                    catch (Exception error) { clean = false; Expect("formation diagnostic dependency cleanup", false, error.ToString()); }
                if (clean) _formationVisualFixture = null;
            }
            Expect("formation fixture complete owner lifecycle", clean, "persistent fixture field retains ownership if a CPU/native cleanup barrier fails");
            File.WriteAllText(output + ".formation-visual.json", JsonSerializer.Serialize(new
            {
                schema = "hollow.formation-visual-native.v1", seconds = clock.Elapsed.TotalSeconds, peakWorkingSet = peak,
                synthetic = true, actualAssetAccepted = false, directory, clean,
                sessionReservation = budget.MaxReservedBytes, diagnosticAllowance = 16 * FormationVisualMiB,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        void Budget()
        {
            process.Refresh(); peak = Math.Max(peak, process.WorkingSet64);
            if (clock.Elapsed.TotalSeconds > 45) throw new TimeoutException("Formation native fixture exceeded 45 seconds.");
            if (process.WorkingSet64 > 1536 * FormationVisualMiB) throw new InvalidOperationException("Formation native fixture exceeded its 1536 MiB process guard.");
        }
        async Task Wait(Func<bool> predicate, string stage)
        {
            for (int step = 0; step < 1024; step++)
            {
                Budget(); if (predicate()) return;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            throw new TimeoutException("Formation native fixture exceeded 1024 pump steps: " + stage);
        }
    }

    private void CheckFormationVisualStreamsAndMaterials(FormationVisualFixtureState state, FormationVisualData reference,
        FormationVisualSession.Lease lease)
    {
        FormationVisualTemplate template = lease.Templates.Single();
        var native = new HashSet<GodotObject>(ReferenceEqualityComparer.Instance);
        foreach (string key in new[] { "near", "far" })
        {
            ArrayMesh mesh = (key == "near" ? template.Near : template.Far).Single().Mesh;
            native.Add(mesh);
            FormationVisualLod source = reference.Lods.Single(lod => lod.LodKey == key);
            bool positions = true, normals = true, tangents = true, uv = true, indices = true, masks = true;
            for (int surface = 0; surface < source.Primitives.Count; surface++)
            {
                FormationVisualPrimitive primitive = source.Primitives[surface];
                using var arrays = mesh.SurfaceGetArrays(surface); // diagnostic only; never runtime authentication
                Vector3[] p = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                Vector3[] n = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                float[] t = arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
                Vector2[] tex = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                int[] ix = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                float[] custom = arrays[(int)Mesh.ArrayType.Custom0].AsFloat32Array();
                positions &= p.Length == 3; normals &= n.Length == 3; tangents &= t.Length == 12; uv &= tex.Length == 3;
                masks &= custom.Length == 12;
                for (int i = 0; i < Math.Min(3, p.Length); i++)
                {
                    positions &= p[i] == new Vector3(primitive.Positions[i * 3], primitive.Positions[i * 3 + 1], primitive.Positions[i * 3 + 2]);
                    if (i < n.Length) normals &= (n[i] - Vector3.Up).Length() < 0.001f;
                    if (i < tex.Length) uv &= tex[i] == new Vector2(primitive.TexCoords[i * 2], primitive.TexCoords[i * 2 + 1]);
                }
                for (int i = 0; i < Math.Min(12, t.Length); i++) tangents &= Math.Abs(t[i] - primitive.Tangents[i]) < 0.001f;
                for (int i = 0; i < Math.Min(12, custom.Length); i++) masks &= BitConverter.SingleToInt32Bits(custom[i]) == BitConverter.SingleToInt32Bits(primitive.Tangents[i]);
                int[] expected = surface == 0 ? new[] { 2, 1, 0 } : new[] { 1, 0, 2 };
                indices &= ix.SequenceEqual(expected);
                var material = mesh.SurfaceGetMaterial(surface) as StandardMaterial3D
                    ?? throw new InvalidOperationException("Expected private StandardMaterial3D surface.");
                native.Add(material);
                if (surface == 0)
                {
                    foreach (Texture2D texture in new[] { material.AlbedoTexture, material.NormalTexture }) native.Add(texture);
                    Expect("formation " + key + " PBR roles preserve factors and channel mapping", material.NormalEnabled && material.AOEnabled &&
                        material.NormalScale == .75f && material.Metallic == .2f && material.Roughness == .65f &&
                        material.CullMode == BaseMaterial3D.CullModeEnum.Back &&
                        material.MetallicTextureChannel == BaseMaterial3D.TextureChannel.Blue &&
                        material.RoughnessTextureChannel == BaseMaterial3D.TextureChannel.Green &&
                        material.AOTextureChannel == BaseMaterial3D.TextureChannel.Red &&
                        ReferenceEquals(material.AlbedoTexture, material.MetallicTexture) &&
                        ReferenceEquals(material.AlbedoTexture, material.RoughnessTexture) && ReferenceEquals(material.AlbedoTexture, material.AOTexture) &&
                        !ReferenceEquals(material.AlbedoTexture, material.NormalTexture),
                        "packed data shares the ordinary mip texture; normal maps use their separately prepared variant");
                }
            }
            Expect("formation " + key + " source order and one winding reversal", positions && normals && tangents && uv && indices && masks && mesh.GetSurfaceCount() == 2,
                "positions/UV/indices/custom0 retain authored order; B/C reversed once; native packed normal/tangent tolerance is 0.001");
        }
        state.NativeOwners.AddRange(native);
        Expect("formation completed owner has exactly two meshes four materials and two textures", native.Count == 8,
            "both LODs share image-role resources while each authored material table has its own immutable material resources");
    }

    private async Task CaptureFormationVisual(FormationVisualFixtureState state, Node3D leftRoot, Node3D rightRoot,
        FormationVisualSession.Lease left, FormationVisualSession.Lease right, string output)
    {
        var viewport = state.Node(new SubViewport());
        viewport.Name = "FormationVisualIsolatedCapture"; viewport.Size = new Vector2I(640, 360); viewport.OwnWorld3D = true;
        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; viewport.Msaa3D = Viewport.Msaa.Disabled;
        viewport.UseTaa = false; viewport.UseDebanding = false; viewport.PositionalShadowAtlasSize = 0;
        AddChild(viewport);
        leftRoot.Reparent(viewport); rightRoot.Reparent(viewport); rightRoot.Position = new Vector3(4, 0, 0);
        var nearMesh = state.Node(new MeshInstance3D()); nearMesh.Mesh = left.Templates[0].Near[0].Mesh; leftRoot.AddChild(nearMesh);
        var farMesh = state.Node(new MeshInstance3D()); farMesh.Mesh = right.Templates[0].Far[0].Mesh; rightRoot.AddChild(farMesh);
        var environment = state.Own(new Godot.Environment());
        environment.BackgroundMode = Godot.Environment.BGMode.Color; environment.BackgroundColor = new Color(.025f, .035f, .045f);
        environment.AmbientLightSource = Godot.Environment.AmbientSource.Color; environment.AmbientLightColor = Colors.White; environment.AmbientLightEnergy = .65f;
        environment.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled; environment.TonemapMode = Godot.Environment.ToneMapper.Linear;
        environment.GlowEnabled = false; environment.FogEnabled = false; environment.SsaoEnabled = false; environment.SsilEnabled = false; environment.SdfgiEnabled = false;
        var world = state.Node(new WorldEnvironment()); world.Environment = environment; viewport.AddChild(world);
        var light = state.Node(new DirectionalLight3D()); light.RotationDegrees = new Vector3(-75, 0, 0); light.LightEnergy = 1.2f;
        light.ShadowEnabled = false; viewport.AddChild(light);
        var camera = state.Node(new Camera3D()); camera.Projection = Camera3D.ProjectionType.Orthogonal;
        camera.Size = 5; camera.Near = .1f; camera.Far = 20; camera.Position = new Vector3(3.5f, 8, 1); camera.Current = true;
        viewport.AddChild(camera); camera.LookAt(new Vector3(3.5f, 0, 1), Vector3.Forward);
        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        using var drawDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        for (int i = 0; i < 3; i++) await ContactGpuNextDraw(drawDeadline.Token);
        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        using Image image = viewport.GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        byte[] pixels = image.GetData();
        int leftPixels = 0, rightPixels = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (Math.Abs(pixels[i] - pixels[0]) + Math.Abs(pixels[i + 1] - pixels[1]) + Math.Abs(pixels[i + 2] - pixels[2]) > 30)
            { if (i / 4 % 640 < 320) leftPixels++; else rightPixels++; }
        string screenshot = output + ".formation-visual.png";
        Error save = image.SavePng(screenshot);
        Expect("formation asymmetric front faces render in both LODs", save == Error.Ok && image.GetWidth() == 640 &&
            image.GetHeight() == 360 && leftPixels > 500 && rightPixels > 500,
            $"{screenshot}; left/right nonbackground pixels={leftPixels}/{rightPixels}; screenshot is synthetic winding/material evidence only");
    }

    private sealed class FormationVisualCleanupProbe(bool failFirst) : IDisposable
    {
        internal int Calls;
        public void Dispose() { Calls++; if (failFirst && Calls == 1) throw new InvalidOperationException("Expected formation cleanup fixture fault."); }
    }

    private void CheckFormationVisualCleanupLedger(FormationVisualFixtureState state)
    {
        var ledger = new FormationVisualLoader.Retirement();
        var first = ledger.Own(new FormationVisualCleanupProbe(false));
        var failing = ledger.Own(new FormationVisualCleanupProbe(true));
        var mesh = ledger.Own(new ArrayMesh());
        // Keep the ledger reachable if an assertion throws before the explicit retry.
        var owner = state.Own(new FormationVisualLoader.Owner(ledger, Array.Empty<FormationVisualTemplate>(), Array.Empty<FormationVisualSurfaceSlot>()));
        bool retired = owner.TryDispose(out Exception? failure);
        Expect("formation private cleanup attempts independent owners and retains failures", !retired && failure != null &&
            first.Calls == 1 && failing.Calls == 1 && !GodotObject.IsInstanceValid(mesh) && owner.OwnedResourceCount == 1,
            "one controlled IDisposable failure leaves only that private owner; independent native mesh still retires");
        bool retried = owner.TryDispose(out Exception? retryError);
        Expect("formation explicit cleanup retry drains retained owner", retried && retryError == null && failing.Calls == 2 && owner.OwnedResourceCount == 0,
            "retry is one bounded pass; this ledger fixture does not replace the production session retirement-barrier checks");
    }

    private void FormationVisualReject(string name, Action action)
    {
        bool refused = false;
        try { action(); }
        catch (FormationValidationException) { refused = true; }
        catch (FormationCapacityException) { refused = true; }
        Expect(name, refused, "expected explicit validation/capacity refusal before new native allocation");
    }

    private static string FormationVisualHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string FormationVisualFloatHash(FormationVisualArray<float> values)
    {
        byte[] bytes = new byte[values.Count * 4];
        for (int i = 0; i < values.Count; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), BitConverter.SingleToInt32Bits(values[i]));
        return FormationVisualHash(bytes);
    }

    private sealed class FormationVisualTinyPackage
    {
        internal string Output { get; }
        private string Source { get; }
        internal string ReceiptPin { get; }
        internal FormationVisualTinyPackage(string root, bool nearest = false)
        {
            Output = Path.Combine(root, "package"); Source = Path.Combine(root, "source");
            Directory.CreateDirectory(Output); Directory.CreateDirectory(Source);
            var views = new JsonArray(); var accessors = new JsonArray(); var primitives = new JsonArray(); var buffers = new JsonArray();
            float[][] positions = { new[] { 0f, 0, 0, 0, 0, 2, 2, 0, 2 }, new[] { 0f, 0, 0, 2, 0, 2, 3, 0, 0 } };
            float[][] uvs = { new[] { 0f, 0, 0, 1, 1, 1 }, new[] { 0f, 0, 1, 1, 1.5f, 0 } };
            var outputNames = new List<string>();
            for (int part = 0; part < 2; part++)
            {
                using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                int Attribute(float[] values, string shape)
                {
                    int view = views.Count, accessor = accessors.Count, offset = checked((int)stream.Length);
                    foreach (float value in values) writer.Write(value);
                    views.Add(N(new { buffer = part, byteOffset = offset, byteLength = values.Length * 4, target = 34962 }));
                    accessors.Add(N(new { bufferView = view, componentType = 5126, count = 3, type = shape }));
                    return accessor;
                }
                var attributes = new JsonObject
                {
                    ["POSITION"] = Attribute(positions[part], "VEC3"),
                    ["NORMAL"] = Attribute(new float[] { 0, 1, 0, 0, 1, 0, 0, 1, 0 }, "VEC3"),
                    ["TANGENT"] = Attribute(new float[] { 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1 }, "VEC4"),
                    ["TEXCOORD_0"] = Attribute(uvs[part], "VEC2"),
                };
                int indexView = views.Count, indexAccessor = accessors.Count, offset = checked((int)stream.Length);
                foreach (ushort index in part == 0 ? new ushort[] { 2, 0, 1 } : new ushort[] { 1, 2, 0 }) writer.Write(index);
                views.Add(N(new { buffer = part, byteOffset = offset, byteLength = 6, target = 34963 }));
                accessors.Add(N(new { bufferView = indexView, componentType = 5123, count = 3, type = "SCALAR" }));
                primitives.Add(new JsonObject { ["attributes"] = attributes, ["indices"] = indexAccessor, ["material"] = part, ["mode"] = 4 });
                string file = "mesh" + part + ".bin"; Write(file, stream.ToArray());
                File.WriteAllBytes(Path.Combine(Source, file), stream.ToArray());
                buffers.Add(N(new { uri = file, byteLength = stream.Length }));
            }
            var document = new JsonObject
            {
                ["asset"] = N(new { version = "2.0" }), ["scene"] = 0,
                ["scenes"] = N(new[] { new { nodes = new[] { 0 } } }), ["nodes"] = N(new[] { new { mesh = 0 } }),
                ["meshes"] = new JsonArray(new JsonObject { ["primitives"] = primitives }), ["buffers"] = buffers,
                ["bufferViews"] = views, ["accessors"] = accessors,
                ["samplers"] = N(new[] { new { magFilter = nearest ? 9728 : 9729, minFilter = 9987, wrapS = 10497, wrapT = 10497 } }),
                ["images"] = N(new[] { new { uri = "image.png", mimeType = "image/png" } }),
                ["textures"] = N(new[] { new { source = 0, sampler = 0 } }),
                ["materials"] = new JsonArray(
                    N(new { name = "isolated-known-pbr", pbrMetallicRoughness = new { baseColorFactor = new[] { .6f, .4f, .2f, 1f },
                        metallicFactor = .2f, roughnessFactor = .65f, baseColorTexture = new { index = 0 }, metallicRoughnessTexture = new { index = 0 } },
                        normalTexture = new { index = 0, scale = .75f }, occlusionTexture = new { index = 0, strength = 1f }, doubleSided = false }),
                    N(new { name = "isolated-known-untextured", pbrMetallicRoughness = new { baseColorFactor = new[] { .15f, .65f, .3f, 1f },
                        metallicFactor = 0f, roughnessFactor = .8f }, emissiveFactor = new[] { .02f, .01f, 0f } })),
            };
            byte[] gltf = JsonSerializer.SerializeToUtf8Bytes(document), png = Png();
            Write("near.gltf", gltf); Write("far.gltf", gltf); Write("image.png", png);
            File.WriteAllBytes(Path.Combine(Source, "source.gltf"), gltf); File.WriteAllBytes(Path.Combine(Source, "image.png"), png);
            object source = new { file = "source.gltf", sha256 = HashFile(Source, "source.gltf"), dependencies = new[] { "mesh0.bin", "mesh1.bin", "image.png" }
                .Select(file => new { file, sha256 = HashFile(Source, file) }).ToArray() };
            File.WriteAllBytes(Path.Combine(Source, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(new { assets = new[] { new { key = "native-fixture", outputs = new[] { source } } } }));
            object physical = new
            {
                vertices = new[] { new[] { 0f, 0, 0 }, new[] { 0f, 0, 2 }, new[] { 2f, 0, 2 }, new[] { 3f, 0, 0 } },
                triangles = new[] { new[] { 2, 0, 1, 0 }, new[] { 2, 3, 0, 0 } },
                patches = new[] { new { key = "bare", substrateProfileKey = "rock", capabilities = Array.Empty<string>() } },
            };
            Write("physical.json", JsonSerializer.SerializeToUtf8Bytes(physical));
            object Lod(string key) => new { key, assetPath = key + ".gltf", contentSha256 = HashFile(Output, key + ".gltf"),
                maximumPositionErrorMetres = 0, minimumNormalDot = .999999, triangleMaps = new[] { new[] { 0, 0 }, new[] { 1, 1 } } };
            object sidecar = new
            {
                schemaVersion = 1, definitions = new[] { new
                {
                    key = "native-fixture", revision = 1, familyKey = "isolated-fixture", sourceSha256 = HashFile(Source, "source.gltf"),
                    physicalSha256 = HashFile(Output, "physical.json"), units = "metres", basis = "right-handed-y-up", minimumScale = 1, maximumScale = 1,
                    physical, domains = new[] { new { patchKey = "bare", boundaries = new[] { new[] { 0, 1, 2, 3 } } } }, contacts = Array.Empty<object>(),
                    lods = new[] { Lod("near"), Lod("far") },
                } },
                profiles = new[] { new { key = "rock", bedrock = 1, looseStone = 0, soil = 0, soilDepthMetres = 0, plantSuitability = 0,
                    grassCoverage = 0, shrubsPerSquareMetre = 0, treesPerSquareMetre = 0, litter = 0, minimumTemperatureC = 0, maximumTemperatureC = 40, minimumSoilUpDot = .6 } },
                recipes = Array.Empty<object>(),
            };
            Write("sidecar.json", JsonSerializer.SerializeToUtf8Bytes(sidecar));
            byte[] receipt = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1, source, manifest = new { file = "manifest.json", sha256 = HashFile(Source, "manifest.json") },
                outputFiles = outputNames.OrderBy(name => name, StringComparer.Ordinal).Select(file => new
                    { file, sha256 = HashFile(Output, file), bytes = new FileInfo(Path.Combine(Output, file)).Length }).ToArray(),
            });
            File.WriteAllBytes(Path.Combine(Output, "qualification-receipt.json"), receipt); ReceiptPin = FormationVisualHash(receipt);
            void Write(string file, byte[] bytes) { File.WriteAllBytes(Path.Combine(Output, file), bytes); outputNames.Add(file); }
        }

        internal FormationPackage Load() => FormationPackageLoader.Load(Output, Source, ReceiptPin, new FormationPackageLimits
            { MaxFiles = 16, MaxFileBytes = 65536, MaxTotalFileBytes = 1048576, MaxJsonBytes = 65536, MaxDefinitions = 1 });
        private static JsonNode N<T>(T value) => JsonSerializer.SerializeToNode(value)!;
        private static string HashFile(string directory, string file) => FormationVisualHash(File.ReadAllBytes(Path.Combine(directory, file)));
        private static byte[] Png()
        {
            using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            byte[] header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, 2); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 2);
            header[8] = 8; header[9] = 6; Chunk("IHDR", header);
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
                zlib.Write(new byte[] { 0, 128, 128, 255, 255, 128, 128, 255, 255, 0, 128, 128, 255, 255, 128, 128, 255, 255 });
            Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>()); return output.ToArray();
            void Chunk(string type, byte[] payload)
            {
                byte[] length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, payload.Length); output.Write(length);
                byte[] tag = Encoding.ASCII.GetBytes(type); output.Write(tag); output.Write(payload); uint crc = uint.MaxValue;
                foreach (byte value in tag.Concat(payload))
                { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u); }
                BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); output.Write(length);
            }
        }
    }
}
