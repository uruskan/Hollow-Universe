using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Opt-in real-package diagnostic. Its external approval record is separate from appearance.json.
    // GetImage/GetData are deliberate diagnostic readbacks here, never a runtime admission mechanism.
    // One-pixel borrowed ground inputs match the export's isolated tree setup; this does not qualify terrain art.
    private NativeAppearanceFixture? _nativeAppearanceFixture;
    private const long NativeAppearanceMiB = 1048576;
    private const long NativeAppearanceBindingBytes = 4096;
    private const long NativeAppearanceGeometryBytes = 320 * NativeAppearanceMiB;
    private const long NativeAppearanceUploadBytes = 96 * NativeAppearanceMiB;
    private const long NativeAppearanceNativeBytes = 768 * NativeAppearanceMiB;
    // Reference payload + one readback's native Image, managed copy and PNG encoding allowance + tiny fixture owners.
    private static long NativeAppearanceDiagnosticBytes => FloraAppearanceSchema.PayloadBytes +
        128 * NativeAppearanceMiB + NativeAppearanceMiB;

    private sealed class NativeAppearanceFixture
    {
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly TaskCompletionSource<bool> Workers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<Node> Nodes = new();
        internal readonly List<IDisposable> Dependencies = new();
        internal readonly List<FloraAppearanceSession.Request> Requests = new();
        internal readonly List<GodotObject> NativeOwners = new();
        internal FloraAppearanceSession? Session;
        internal Task<FloraAppearancePackage>? ReferenceLoad;
        internal FloraAppearancePackage? Reference;
        internal bool SawQuarantine;
        internal T Own<T>(T value) where T : IDisposable { Dependencies.Add(value); return value; }
        internal T OwnNode<T>(T value) where T : Node { Nodes.Add(value); return value; }
    }

    // Coordinator-owned dispatch: await CheckNativeFloraAppearance(approvalPath, independentApprovalSha256),
    // followed by WriteReport("native-flora-appearance-package"). Do not call alongside a live world.
    private async Task CheckNativeFloraAppearance(string approvalPath, string independentApprovalSha256)
    {
        if (_nativeAppearanceFixture != null) throw new InvalidOperationException("Native appearance fixture is single-use while owners remain.");
        NativeFloraAppearanceApproval approval = NativeFloraAppearanceApproval.Read(approvalPath, independentApprovalSha256);
        FloraAppearanceCatalog catalog = FloraAppearanceCatalog.EarthLegacy(approval.Pin);
        NativeAppearanceRequire(catalog.Identity == approval.ExpectedCatalogIdentity, "Independent catalog identity differs.");
        NativeAppearanceRequire(FloraAppearanceInputs.RuntimeFingerprint(approval.Pin.ExpectedInputs) == approval.ExpectedRuntimeFingerprint,
            "Independent runtime input identity differs.");
        var limits = new FloraAppearanceLimits(1048576, 67108864, 134217728);
        var inputLimits = new FloraAppearanceInputLimits(1048576, 268435456, 1073741824, 33554432, 262144, 1048576);
        long entry = checked(FloraAppearanceSchema.PayloadBytes + NativeAppearanceGeometryBytes +
            NativeAppearanceUploadBytes + NativeAppearanceNativeBytes);
        var budget = new FloraAppearanceSessionBudget(2, 2, entry + 2 * NativeAppearanceBindingBytes,
            NativeAppearanceGeometryBytes, NativeAppearanceUploadBytes, NativeAppearanceNativeBytes, NativeAppearanceBindingBytes);
        string output = Path.GetFullPath(OutputPath);
        string readbacks = output + ".textures";
        if (Directory.Exists(readbacks) || File.Exists(readbacks))
            throw new IOException("Real appearance readback output must be a new directory.");

        var fixture = new NativeAppearanceFixture();
        _nativeAppearanceFixture = fixture;
        try
        {
            Expect("real appearance independent approval", true,
                $"manifest={approval.Pin.ManifestSha256}; inputs={approval.Pin.ExpectedInputs.Fingerprint}; " +
                $"runtime={approval.ExpectedRuntimeFingerprint}; catalog={catalog.Identity}");
            Expect("real appearance declared reservation", true,
                $"sessionCeiling={budget.MaxReservedBytes}; additionalDiagnosticAllowance={NativeAppearanceDiagnosticBytes}; " +
                "native allowance includes private imported resources; allowances are not measured driver-memory bounds");

            // This independently authenticated reference is deliberately charged in addition to the session's copy.
            string directory = approval.PackageDirectory;
            FloraAppearancePin pin = approval.Pin;
            CancellationToken token = fixture.Cancellation.Token;
            fixture.ReferenceLoad = Task.Run(() => FloraAppearancePackageLoader.Load(directory, pin, limits, token));
            await NativeAppearanceUntil(() => fixture.ReferenceLoad.IsCompleted, "real appearance reference payload load");
            fixture.Reference = await fixture.ReferenceLoad;
            Directory.CreateDirectory(readbacks);

            SetupNativeAppearanceGlobals(fixture);
            Texture2DArray groundAlbedo = NativeAppearanceArray(fixture, Colors.White);
            Texture2DArray groundNormal = NativeAppearanceArray(fixture, new Color(0.5f, 0.5f, 1));
            var session = new FloraAppearanceSession(catalog, directory, budget, limits, inputLimits, groundAlbedo, groundNormal);
            fixture.Session = session;
            Node3D leftRoot = NativeAppearanceRoot(fixture, "NativeAppearanceLeft");
            Node3D rightRoot = NativeAppearanceRoot(fixture, "NativeAppearanceRight");

            CheckNativeAppearanceRejections(session, catalog, leftRoot);
            FloraAppearanceSession.Request leftRequest = NativeAppearanceAcquire(fixture, catalog, leftRoot);
            FloraAppearanceSession.Request rightRequest = NativeAppearanceAcquire(fixture, catalog, rightRoot);
            FloraAppearanceSession.Snapshot admitted = session.CaptureDiagnostics();
            Expect("real appearance reserves before publication", admitted.LoadStarts == 1 && admitted.UploadStarts == 0 &&
                admitted.TotalBytes == budget.MaxReservedBytes && admitted.Acquisitions == 2,
                "both requests reserve payload, geometry, upload, native and binding allowances before Pump uploads");
            await NativeAppearanceUntil(() =>
            {
                session.Pump(); return leftRequest.Ready.IsCompleted && rightRequest.Ready.IsCompleted;
            }, "real appearance production load and native creation");
            FloraAppearanceSession.Lease left = await leftRequest.Ready, right = await rightRequest.Ready;
            FloraLibrary library = left.Library;
            ShaderMaterial leftMaterial = left.ImpostorMaterial, rightMaterial = right.ImpostorMaterial;
            fixture.NativeOwners.Add(leftMaterial); fixture.NativeOwners.Add(rightMaterial);
            FloraAppearanceSession.Snapshot loaded = session.CaptureDiagnostics();
            Expect("real appearance production resources are shared", ReferenceEquals(library, right.Library) &&
                loaded.Ready && loaded.LoadStarts == 1 && loaded.UploadStarts == 1 &&
                loaded.GeometryBytes == 0 && loaded.UploadBytes == 0,
                "production constructor completed CreateFromAppearance/ValidateRuntime; no injected backend, paint or bake path");
            float originalFade = rightMaterial.GetShaderParameter("fade_out").AsSingle();
            leftMaterial.SetShaderParameter("fade_out", originalFade + 17f);
            Expect("real appearance mutable lease bindings are isolated", leftMaterial.GetInstanceId() != rightMaterial.GetInstanceId() &&
                rightMaterial.GetShaderParameter("fade_out").AsSingle() == originalFade,
                "changing one lease's fade parameter leaves its sibling unchanged");
            leftMaterial.SetShaderParameter("fade_out", originalFade);
            CheckNativeAppearanceRows(fixture, library, approval.Pin.ExpectedInputs, leftMaterial, rightMaterial);
            FloraAppearanceColour mean = fixture.Reference.BladeMean;
            Expect("real appearance blade mean preserves float bits", NativeAppearanceFloat(library.BladeMean.X, mean.R) &&
                NativeAppearanceFloat(library.BladeMean.Y, mean.G) && NativeAppearanceFloat(library.BladeMean.Z, mean.B),
                "exact admitted RGB components, without repainting or recomputing the mean");

            var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal)
            {
                ["near-atlas"] = library.AtlasTexture,
                ["bark"] = NativeAppearanceTexture(library.Bark, "albedo_texture"),
                ["bark-normal"] = NativeAppearanceTexture(library.Bark, "normal_texture"),
                ["birch"] = NativeAppearanceTexture(library.BirchBark, "albedo_texture"),
                ["birch-normal"] = NativeAppearanceTexture(library.BirchBark, "normal_texture"),
                ["impostor-colour"] = NativeAppearanceTexture(leftMaterial, "colour_atlas"),
                ["impostor-normal"] = NativeAppearanceTexture(leftMaterial, "normal_atlas"),
            };
            Expect("real appearance far textures are shared", textures["impostor-colour"].GetInstanceId() ==
                NativeAppearanceTexture(rightMaterial, "colour_atlas").GetInstanceId() &&
                textures["impostor-normal"].GetInstanceId() == NativeAppearanceTexture(rightMaterial, "normal_atlas").GetInstanceId(),
                "distinct materials borrow the same two owned native atlases");
            foreach (FloraAppearanceImage payload in fixture.Reference.Images)
            {
                Texture2D texture = textures[payload.Layout.Role]; fixture.NativeOwners.Add(texture);
                CheckNativeAppearanceReadback(texture, payload, readbacks);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            AttachNativeAppearanceConsumers(fixture, leftRoot, left);
            AttachNativeAppearanceConsumers(fixture, rightRoot, right);

            Task leftRetired = left.Retire(fixture.Workers.Task);
            leftRoot.QueueFree(); session.Pump();
            Expect("real appearance queued root retains resources", !leftRetired.IsCompleted &&
                GodotObject.IsInstanceValid(leftRoot) && GodotObject.IsInstanceValid(leftMaterial),
                "QueueFree alone does not complete the lease");
            await NativeAppearanceUntil(() => { session.Pump(); return !GodotObject.IsInstanceValid(leftRoot); },
                "real appearance first consumer destruction");
            Expect("real appearance worker completion is an independent barrier", !leftRetired.IsCompleted &&
                GodotObject.IsInstanceValid(leftMaterial) && session.CaptureDiagnostics().Acquisitions == 2,
                "deleted consumers do not release an incomplete worker's lease or reservations");
            fixture.Workers.TrySetResult(true);
            await NativeAppearanceUntil(() => { session.Pump(); return leftRetired.IsCompleted; }, "real appearance first lease retirement");
            await leftRetired;
            Expect("real appearance sibling survives retirement", !GodotObject.IsInstanceValid(leftMaterial) &&
                GodotObject.IsInstanceValid(rightMaterial) && GodotObject.IsInstanceValid(textures["near-atlas"]),
                "retiring one binding leaves the shared native library available to its sibling");

            Node3D replacementRoot = NativeAppearanceRoot(fixture, "NativeAppearanceReplacement");
            FloraAppearanceSession.Request replacementRequest = NativeAppearanceAcquire(fixture, catalog, replacementRoot);
            await NativeAppearanceUntil(() => { session.Pump(); return replacementRequest.Ready.IsCompleted; }, "real appearance warm reacquisition");
            FloraAppearanceSession.Lease replacement = await replacementRequest.Ready;
            fixture.NativeOwners.Add(replacement.ImpostorMaterial);
            AttachNativeAppearanceConsumers(fixture, replacementRoot, replacement);
            Expect("real appearance view replacement reuses one upload", ReferenceEquals(library, replacement.Library) &&
                session.CaptureDiagnostics().LoadStarts == 1 && session.CaptureDiagnostics().UploadStarts == 1,
                "a replacement consumer receives the still-owned package without another paint, bake, load or upload");
        }
        finally { await RetireNativeAppearanceFixture(fixture); }
    }

    private void CheckNativeAppearanceRejections(FloraAppearanceSession session, FloraAppearanceCatalog catalog, Node root)
    {
        string wrong = catalog.Identity[0] == 'A' ? new string('B', 64) : new string('A', 64);
        bool identity = AppearanceFixtureReject<FloraAppearanceValidationException>(() => session.Acquire(
            FloraAppearanceCatalog.EarthWorld, catalog.VisualVersion, wrong, root));
        bool version = AppearanceFixtureReject<FloraAppearanceValidationException>(() => session.Acquire(
            FloraAppearanceCatalog.EarthWorld, "unsupported-fixture-version", catalog.Identity, root));
        bool world = AppearanceFixtureReject<FloraAppearanceValidationException>(() => session.Acquire(
            FloraAppearanceCatalog.EarthWorld with { SurfaceSeed = FloraAppearanceCatalog.EarthWorld.SurfaceSeed + 1 },
            catalog.VisualVersion, catalog.Identity, root));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        bool cancelled = AppearanceFixtureReject<OperationCanceledException>(() => session.Acquire(
            FloraAppearanceCatalog.EarthWorld, catalog.VisualVersion, catalog.Identity, root, cancellation.Token));
        FloraAppearanceSession.Snapshot state = session.CaptureDiagnostics();
        Expect("real appearance unsupported requests dispatch no work", identity && version && world && cancelled &&
            state.TotalBytes == 0 && state.LoadStarts == 0 && state.UploadStarts == 0,
            "wrong world/version/catalog and pre-cancellation cannot trigger a fallback");
    }

    private void CheckNativeAppearanceRows(NativeAppearanceFixture fixture, FloraLibrary library,
        FloraAppearanceInputContract inputs, ShaderMaterial left, ShaderMaterial right)
    {
        bool exact = library.All.Where(s => s.Tree).Sum(s => s.Variants.Count) == FloraAppearanceSchema.Rows;
        foreach (FloraAppearanceRow row in inputs.Rows)
        {
            FloraLibrary.Species species = library.For(row.Kind) ?? throw new FloraAppearanceValidationException("Missing qualified tree species.");
            NativeAppearanceRequire((uint)row.Variant < (uint)species.Variants.Count, "Missing qualified tree variant.");
            FloraLibrary.Variant variant = species.Variants[row.Variant];
            exact &= variant.ImpostorRow == row.Row && NativeAppearanceFloat(species.Scale, row.SpeciesScale) &&
                NativeAppearanceFloat(variant.Height, row.Height) && NativeAppearanceFloat(variant.Extent, row.Extent);
            fixture.NativeOwners.Add(variant.Lod0); fixture.NativeOwners.Add(variant.Lod1);
        }
        Vector2 native = new(FloraAppearanceSchema.NativeRowStart, FloraAppearanceSchema.NativeRowEnd);
        foreach (ShaderMaterial material in new[] { left, right })
            exact &= material.GetShaderParameter("rows").AsSingle() == FloraAppearanceSchema.Rows &&
                material.GetShaderParameter("views").AsSingle() == FloraAppearanceSchema.Views &&
                material.GetShaderParameter("native_rows").AsVector2() == native;
        Expect("real appearance ordered rows and interpretation", exact,
            "all eleven row bindings, native interval, eight views, species scales and variant dimensions match the independent input contract");
    }

    private void CheckNativeAppearanceReadback(Texture2D texture, FloraAppearanceImage expected, string directory)
    {
        FloraAppearanceImageLayout layout = expected.Layout;
        using Image actual = texture.GetImage() ?? throw new InvalidOperationException("Native texture readback returned no image.");
        bool layoutMatches = actual.GetWidth() == layout.Width && actual.GetHeight() == layout.Height &&
            actual.GetFormat() == Image.Format.Rgba8 && actual.HasMipmaps() &&
            actual.GetMipmapCount() + 1 == layout.MipLevels && actual.GetDataSize() == layout.ByteLength;
        // Reject unexpected native storage before allocating a managed readback copy.
        NativeAppearanceRequire(layoutMatches, "Native image layout mismatch: " + layout.Role);
        byte[] bytes = actual.GetData();
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        bool exact = bytes.Length == expected.ByteLength && digest == expected.Sha256;
        Expect("real appearance complete native readback: " + layout.Role, exact,
            $"bytes={bytes.Length}; levels={layout.MipLevels}; expected={expected.Sha256}; actual={digest}");
        string path = Path.Combine(directory, layout.Role + ".png");
        if (actual.SavePng(path) != Error.Ok) throw new IOException("Cannot save native readback evidence: " + path);
    }

    private Node3D NativeAppearanceRoot(NativeAppearanceFixture fixture, string name)
    {
        Node3D root = fixture.OwnNode(new Node3D()); root.Name = name; root.Visible = false; AddChild(root); return root;
    }

    private static FloraAppearanceSession.Request NativeAppearanceAcquire(NativeAppearanceFixture fixture,
        FloraAppearanceCatalog catalog, Node root)
    {
        FloraAppearanceSession.Request request = fixture.Session!.Acquire(FloraAppearanceCatalog.EarthWorld,
            catalog.VisualVersion, catalog.Identity, root, fixture.Cancellation.Token);
        fixture.Requests.Add(request); return request;
    }

    private static void AttachNativeAppearanceConsumers(NativeAppearanceFixture fixture, Node3D root, FloraAppearanceSession.Lease lease)
    {
        foreach (FloraLibrary.Species species in lease.Library.All.Where(species => species.Tree))
        foreach (FloraLibrary.Variant variant in species.Variants)
        foreach (ArrayMesh mesh in new[] { variant.Lod0, variant.Lod1 })
        {
            MeshInstance3D node = fixture.OwnNode(new MeshInstance3D()); root.AddChild(node); node.Mesh = mesh;
        }
        QuadMesh quad = fixture.Own(new QuadMesh());
        MeshInstance3D far = fixture.OwnNode(new MeshInstance3D()); root.AddChild(far);
        far.Mesh = quad; far.MaterialOverride = lease.ImpostorMaterial;
    }

    private static Texture2D NativeAppearanceTexture(ShaderMaterial material, string name) =>
        material.GetShaderParameter(name).AsGodotObject() as Texture2D ??
        throw new FloraAppearanceValidationException("Missing native texture binding: " + name);

    private static Texture2DArray NativeAppearanceArray(NativeAppearanceFixture fixture, Color colour)
    {
        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8); image.Fill(colour);
        var images = new Godot.Collections.Array<Image> { image };
        using var storage = (Godot.Collections.Array)images;
        Texture2DArray texture = fixture.Own(new Texture2DArray());
        if (texture.CreateFromImages(images) != Error.Ok) throw new InvalidOperationException("Diagnostic ground input allocation failed.");
        return texture;
    }

    private static void SetupNativeAppearanceGlobals(NativeAppearanceFixture fixture)
    {
        PlanetGlobals.Register();
        ImageTexture Solid(Color colour)
        {
            using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8); image.Fill(colour);
            return fixture.Own(ImageTexture.CreateFromImage(image));
        }
        ImageTexture3D Volume(Color colour)
        {
            using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8); image.Fill(colour);
            var images = new Godot.Collections.Array<Image> { image };
            using var storage = (Godot.Collections.Array)images;
            ImageTexture3D texture = fixture.Own(new ImageTexture3D());
            if (texture.Create(Image.Format.Rgba8, 1, 1, 1, false, images) != Error.Ok)
                throw new InvalidOperationException("Diagnostic atmosphere input allocation failed.");
            return texture;
        }
        Texture2D zero = Solid(Colors.Black), one = Solid(Colors.White), noise = Solid(new Color(0.7f, 0.7f, 0.7f));
        Texture3D dark = Volume(Colors.Black), clear = Volume(Colors.White);
        // Added last so reverse cleanup unregisters globals before disposing their borrowed input textures.
        SurfaceDiagnosticGlobals globals = fixture.Own(new SurfaceDiagnosticGlobals());
        globals.Setup(PlanetGlobals.Radius, zero, one, noise, dark, clear);
    }

    private async Task RetireNativeAppearanceFixture(NativeAppearanceFixture fixture)
    {
        fixture.Workers.TrySetResult(true); fixture.Cancellation.Cancel();
        FloraAppearanceSession? session = fixture.Session;
        session?.Stop();
        foreach (FloraAppearanceSession.Request request in fixture.Requests)
        {
            request.Cancel();
            if (request.Ready.IsCompletedSuccessfully) _ = request.Ready.Result.Retire(Task.CompletedTask);
            else if (request.Ready.IsFaulted) _ = request.Ready.Exception;
        }
        foreach (Node node in fixture.Nodes)
            if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion()) node.QueueFree();
        try
        {
            await NativeAppearanceUntil(() =>
            {
                session?.Pump();
                if (session?.CaptureDiagnostics().Quarantined == true && !fixture.SawQuarantine)
                {
                    fixture.SawQuarantine = true;
                    Expect("real appearance cleanup has no quarantine", false, "native cleanup failed; attempting one explicit retry while charges remain held");
                    session.RetryCleanup();
                }
                return (fixture.ReferenceLoad == null || fixture.ReferenceLoad.IsCompleted) &&
                    (session == null || session.Stopped.IsCompleted && session.CaptureDiagnostics().TotalBytes == 0) &&
                    fixture.Nodes.All(node => !GodotObject.IsInstanceValid(node));
            }, "real appearance complete native retirement");
        }
        catch (Exception error)
        {
            Expect("real appearance complete native retirement", false, error.ToString());
            return; // Preserve session, borrowed dependencies, cancellation and pending owners on this live fixture.
        }
        if (fixture.ReferenceLoad?.IsFaulted == true) _ = fixture.ReferenceLoad.Exception;
        bool stopped = session == null || session.Stopped.IsCompletedSuccessfully;
        if (session?.Stopped.IsFaulted == true) _ = session.Stopped.Exception;
        foreach (FloraAppearanceSession.Request request in fixture.Requests)
            if (request.Ready.IsFaulted) _ = request.Ready.Exception;
        bool nativeGone = fixture.NativeOwners.All(resource => !GodotObject.IsInstanceValid(resource));
        Expect("real appearance session owns complete retirement", stopped && !fixture.SawQuarantine && nativeGone &&
            (session == null || session.CaptureDiagnostics().Acquisitions == 0),
            "all published lease materials, seven image textures and captured tree meshes are invalid; zero session reservations");
        for (int i = fixture.Dependencies.Count - 1; i >= 0; i--)
        {
            try { fixture.Dependencies[i].Dispose(); fixture.Dependencies.RemoveAt(i); }
            catch (Exception error) { Expect("real appearance diagnostic dependency retirement", false, error.ToString()); }
        }
        fixture.Reference = null; fixture.ReferenceLoad = null;
        fixture.Requests.Clear(); fixture.Nodes.Clear(); fixture.NativeOwners.Clear();
        if (fixture.Dependencies.Count == 0)
        {
            fixture.Cancellation.Dispose(); fixture.Session = null; _nativeAppearanceFixture = null;
        }
    }

    private async Task NativeAppearanceUntil(Func<bool> condition, string stage)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed.TotalSeconds > 45) throw new TimeoutException(stage);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static bool NativeAppearanceFloat(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    private static void NativeAppearanceRequire(bool condition, string message)
    { if (!condition) throw new FloraAppearanceValidationException(message); }
}
