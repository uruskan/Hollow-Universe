using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace HollowUniverse.Surface.Flora;

/// <summary>
/// Renders every tree variant of the flora library from eight sides into two atlases (GROUND.md §4.6): colour with
/// alpha, and camera-space normals; one row per variant, one column per view. Done once when the surface view opens,
/// in an offscreen viewport of its own world. One atlas row is rendered at a time; complete pass images retain
/// the original dimensions and processing, including dilation across row boundaries.
/// </summary>
public sealed partial class ImpostorBaker : Node
{
    public const int Views = 8;
    public const int CellPixels = 256;
    private const float Spacing = 100; // metres between cells in the bake world (trees are scaled to fit)

    public sealed class Result : IDisposable
    {
        public ImageTexture Colour = null!;
        public ImageTexture Normal = null!;
        public int Rows;
        public Vector2 NativeRows;
        public void Dispose() { Colour?.Dispose(); Normal?.Dispose(); }
    }

    // Per-invocation observers/fault injection for the bounded native ownership fixture. Production calls
    // supply no hooks; no static callback or test state can affect a concurrent real bake.
    internal sealed class OwnershipFixtureHooks
    {
        public Action<SubViewport>? ViewportCreated { get; init; }
        public Action<int, Image>? AssemblyCreated { get; init; }
        public Action<int, int, Image>? BandReadback { get; init; }
        public Action<int, Image>? Readback { get; init; }
        public Action<SubViewport>? ViewportRetiring { get; init; }
        public Action? RetirementWaitArmed { get; init; }
        public Action<int>? ProcessingStarting { get; init; }
        public Action<int, Image>? BeforeGetData { get; init; }
        public Action<int, Image>? BeforePixels { get; init; }
        public Action<int, ImageTexture>? Uploaded { get; init; }
    }

    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started, _retiring, _completed;
    private Result? _ownedResult;
    public Task Stopped => _stopped.Task;

    /// <summary>Main-thread cancellation. No waiting for render signals or atlas processing.</summary>
    public void Stop()
    {
        if (_retiring) return;
        _retiring = true;
        if (!_completed) _cancellation.Cancel();
        if (!_started || _completed)
        {
            _ownedResult?.Dispose(); _ownedResult = null;
            if (!_completed) _cancellation.Dispose();
            _completed = true;
            _stopped.TrySetResult(true);
        }
    }

    public override void _ExitTree() => Stop();

    /// <summary>Transfers atlas ownership to the caller on the main thread, once Bake completed successfully.</summary>
    public Result TakeResult()
    {
        if (_retiring || !_completed || _ownedResult == null) throw new InvalidOperationException("No live impostor result to take.");
        Result result = _ownedResult; _ownedResult = null;
        return result;
    }

    /// <summary>Bakes the atlases; assigns each tree variant its row.</summary>
    public Task<Result> Bake(FloraLibrary library)
        => BakeLibrary(library, null);

    /// <summary>Isolated asset export observes the exact finished image, including its existing mip chain.</summary>
    internal Task<Result> BakeForExport(FloraLibrary library, Action<int, Image> export)
        => BakeLibrary(library, export ?? throw new ArgumentNullException(nameof(export)));

    private Task<Result> BakeLibrary(FloraLibrary library, Action<int, Image>? export)
    {
        var variants = new List<(FloraLibrary.Species Species, FloraLibrary.Variant Variant)>();
        foreach (FloraLibrary.Species species in library.All)
        {
            if (!species.Tree) continue;
            foreach (FloraLibrary.Variant variant in species.Variants) variants.Add((species, variant));
        }
        return BeginBake(variants, export: export);
    }

    private Task<Result> BeginBake(List<(FloraLibrary.Species Species, FloraLibrary.Variant Variant)> variants,
        OwnershipFixtureHooks? fixture = null, bool fixtureFullAtlas = false, Action<int, Image>? export = null)
    {
        if (_started || _retiring) throw new InvalidOperationException("Impostor baking is single-use.");
        _started = true;
        for (int row = 0; row < variants.Count; row++) variants[row].Variant.ImpostorRow = row;
        Task<Result> task = BakeCore(variants, _cancellation.Token, fixture, fixtureFullAtlas, export);
        _ = task.ContinueWith(static finished => { _ = finished.Exception; }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    // One triangle using the actual bake setup/wait/cleanup path. Ownership hooks opt into tiny atlas dimensions;
    // the original lifecycle probe still cancels before any draw with its existing default setup.
    internal Task<Result> BakeLifecycleFixture(OwnershipFixtureHooks? fixture = null)
    {
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0, 1, 0) };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { Vector2.Zero, Vector2.Right, Vector2.Down };
        var shader = new Shader { Code = "shader_type spatial; uniform int mode = 2; void fragment() { ALBEDO = vec3(0.3); }" };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("mode", 2);
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        var variant = new FloraLibrary.Variant { Lod0 = mesh, Lod1 = mesh, Height = 1, Extent = 1 };
        var species = new FloraLibrary.Species { Tree = true };
        species.Variants.Add(variant);
        return BeginBake(new() { (species, variant) }, fixture);
    }

    // Three deliberately different rows exercise the actual bake shader, camera, readbacks and complete Finish.
    // The full-atlas switch exists only on this tiny asset-free oracle; normal Bake has no quality/configuration fork.
    internal async Task<Result> BakeRowBandFixture(bool fullAtlas, OwnershipFixtureHooks fixture)
    {
        var resources = new List<GodotObject>();
        try
        {
            var shader = new Shader { Code = "shader_type spatial; uniform int mode = 0; uniform sampler2D albedo_texture; void fragment() { ALBEDO = vec3(0.3); }" };
            resources.Add(shader);
            var bytes = new byte[8 * 8 * 4];
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int p = (y * 8 + x) * 4;
                bytes[p] = (byte)(63 + x * 19); bytes[p + 1] = (byte)(77 + y * 17); bytes[p + 2] = 151;
                bytes[p + 3] = (byte)((x + y * 3) % 7 == 0 ? 0 : (x + y) % 5 == 0 ? 110 : 255);
            }
            using Image image = Image.CreateFromData(8, 8, false, Image.Format.Rgba8, bytes);
            ImageTexture texture = ImageTexture.CreateFromImage(image);
            resources.Add(texture);
            var variants = new List<(FloraLibrary.Species Species, FloraLibrary.Variant Variant)>();
            for (int row = 0; row < 3; row++)
            {
                var material = new ShaderMaterial { Shader = shader };
                resources.Add(material);
                material.SetShaderParameter("mode", 0);
                material.SetShaderParameter("albedo_texture", texture);
                using var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                float height = 0.72f + row * 0.11f;
                arrays[(int)Mesh.ArrayType.Vertex] = new[]
                {
                    new Vector3(-0.43f + row * 0.07f, 0.03f, 0.04f), new Vector3(0.38f, 0.09f + row * 0.03f, -0.08f),
                    new Vector3(-0.19f + row * 0.11f, height, 0.07f),
                    new Vector3(-0.32f, 0.17f, -0.22f), new Vector3(0.29f - row * 0.06f, 0.37f, 0.24f),
                    new Vector3(0.07f, height * 0.79f, -0.13f),
                };
                arrays[(int)Mesh.ArrayType.Normal] = new[]
                {
                    new Vector3(0.1f, 0.2f, 1).Normalized(), new Vector3(0.1f, 0.2f, 1).Normalized(), new Vector3(0.1f, 0.2f, 1).Normalized(),
                    new Vector3(-0.5f, 0.15f, 1).Normalized(), new Vector3(-0.5f, 0.15f, 1).Normalized(), new Vector3(-0.5f, 0.15f, 1).Normalized(),
                };
                arrays[(int)Mesh.ArrayType.TexUV] = new[]
                { new Vector2(0.03f, 0.91f), new Vector2(0.97f, 0.83f), new Vector2(0.29f, 0.07f),
                  new Vector2(0.13f, 0.86f), new Vector2(0.84f, 0.71f), new Vector2(0.61f, 0.11f) };
                Color colour = row == 0 ? new Color(0.91f, 0.23f, 0.13f) : row == 1
                    ? new Color(0.17f, 0.83f, 0.29f) : new Color(0.21f, 0.31f, 0.93f);
                arrays[(int)Mesh.ArrayType.Color] = new[] { colour, colour, colour, colour, colour, colour };
                var mesh = new ArrayMesh();
                resources.Add(mesh);
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                mesh.SurfaceSetMaterial(0, material);
                var variant = new FloraLibrary.Variant { Lod0 = mesh, Lod1 = mesh, Height = height, Extent = 1.13f };
                var species = new FloraLibrary.Species { Tree = true };
                species.Variants.Add(variant);
                variants.Add((species, variant));
            }
            return await BeginBake(variants, fixture, fullAtlas);
        }
        finally
        {
            for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
        }
    }

    private async Task<Result> BakeCore(List<(FloraLibrary.Species Species, FloraLibrary.Variant Variant)> variants,
        CancellationToken cancellation, OwnershipFixtureHooks? fixture, bool fixtureFullAtlas, Action<int, Image>? export)
    {
        SubViewport? viewport = null;
        // Own a completed readback until its processing task has accepted ownership. This also covers
        // cancellation while waiting for the final viewport's queued destruction.
        Image? pendingReadback = null;
        Image? pendingBand = null;
        Result? result = null;
        bool success = false;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            int rows = variants.Count;
            if (rows < 1) throw new InvalidOperationException("An impostor atlas requires at least one tree variant.");
            int cellPixels = fixture == null ? CellPixels : 8;
            // The full-size path is a tiny, explicitly invoked fixture oracle only. Production always owns
            // one row of framebuffer attachments, regardless of the number of variants in the final atlas.
            int viewportRows = fixture != null && fixtureFullAtlas ? rows : 1;
            bool trace = Array.IndexOf(OS.GetCmdlineUserArgs(), "--capture-trace") >= 0;
            TraceAtlas(trace, "bake-begin", -1, Views * cellPixels, rows * cellPixels);
            RenderMemoryTrace.Mark("flora-atlas:viewport-before-allocation");
            viewport = new SubViewport
            {
                Size = new Vector2I(Views * cellPixels, viewportRows * cellPixels),
                TransparentBg = true,
                OwnWorld3D = true,
                Msaa3D = Viewport.Msaa.Disabled,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                PositionalShadowAtlasSize = 0,
            };
            AddChild(viewport);
            fixture?.ViewportCreated?.Invoke(viewport);
            RenderMemoryTrace.Mark("flora-atlas:viewport-added");
            TraceAtlas(trace, "viewport-band", -1, Views * cellPixels, viewportRows * cellPixels);
            var camera = new Camera3D
            {
                Projection = Camera3D.ProjectionType.Orthogonal,
                Size = viewportRows * Spacing,
                KeepAspect = Camera3D.KeepAspectEnum.Height,
                Near = 1,
                Far = 4 * Spacing,
                Position = new Vector3(Views * Spacing / 2, (rows - viewportRows * 0.5f) * Spacing, 2 * Spacing),
                Current = true,
            };
            viewport.AddChild(camera);

            Shader shader = GD.Load<Shader>("res://shaders/surface_impostor_bake.gdshader");
            var materials = new List<ShaderMaterial>();
            var bySource = new Dictionary<ShaderMaterial, ShaderMaterial>();
            ShaderMaterial Bake(ShaderMaterial source)
            {
                if (bySource.TryGetValue(source, out ShaderMaterial? cached)) return cached;
                int mode = (int)source.GetShaderParameter("mode");
                var m = new ShaderMaterial { Shader = shader };
                m.SetShaderParameter("mode", mode);
                if (mode != 2)
                {
                    m.SetShaderParameter("albedo_texture", source.GetShaderParameter("albedo_texture"));
                }
                if (source.HasMeta("native_tree"))
                {
                    m.SetShaderParameter("albedo_factor", source.GetMeta("bake_albedo_factor"));
                    m.SetShaderParameter("alpha_cutoff", source.GetMeta("bake_alpha_cutoff"));
                    m.SetShaderParameter("vertex_colour", false);
                }
                materials.Add(m);
                bySource.Add(source, m);
                return m;
            }
            int nativeStart = rows, nativeEnd = rows;

            for (int r = 0; r < rows; r++)
            {
                (FloraLibrary.Species species, FloraLibrary.Variant variant) = variants[r];
                bool native = variant.Lod0.SurfaceGetMaterial(0).HasMeta("native_tree");
                if (native)
                {
                    nativeStart = Math.Min(nativeStart, r);
                    nativeEnd = r + 1;
                }
                else if (nativeStart < r)
                    throw new InvalidOperationException("Native tree atlas rows must be contiguous.");
                float k = Spacing / variant.Extent;
                for (int c = 0; c < Views; c++)
                {
                    // View c shows the tree as seen from azimuth c/8 of a turn (atan2(x, z) in its own frame): the camera
                    // looks along -Z, so the tree turns the other way.
                    float azimuth = c * Mathf.Tau / Views;
                    var basis = new Basis(Vector3.Up, -azimuth).Scaled(new Vector3(k, k, k));
                    var origin = new Vector3((c + 0.5f) * Spacing, (rows - r - 0.5f) * Spacing - k * variant.Height / 2, 0);
                    var mi = new MeshInstance3D { Mesh = variant.Lod0, Transform = new Transform3D(basis, origin) };
                    for (int s = 0; s < variant.Lod0.GetSurfaceCount(); s++)
                    {
                        if (variant.Lod0.SurfaceGetMaterial(s) is not ShaderMaterial source)
                            throw new InvalidOperationException("Tree surface has no bakeable material.");
                        mi.SetSurfaceOverrideMaterial(s, Bake(source));
                    }
                    viewport.AddChild(mi);
                }
            }

            result = new Result { Rows = rows, NativeRows = new Vector2(nativeStart, nativeEnd) };
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (ShaderMaterial m in materials)
                {
                    m.SetShaderParameter("pass", pass);
                }
                pendingReadback = Image.CreateEmpty(Views * cellPixels, rows * cellPixels, false, Image.Format.Rgba8);
                fixture?.AssemblyCreated?.Invoke(pass, pendingReadback);
                for (int firstRow = 0; firstRow < rows; firstRow += viewportRows)
                {
                    cancellation.ThrowIfCancellationRequested();
                    // Preserve every mesh's original world coordinates. Cropping only the orthographic camera
                    // keeps its pixels/metre, view rotation, near/far planes and cross-row geometry unchanged.
                    camera.Position = new Vector3(Views * Spacing / 2,
                        (rows - firstRow - viewportRows * 0.5f) * Spacing, 2 * Spacing);
                    viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                    for (int f = 0; f < 3; f++)
                    {
                        await NextDraw(cancellation);
                        cancellation.ThrowIfCancellationRequested();
                    }
                    pendingBand = viewport.GetTexture().GetImage();
                    fixture?.BandReadback?.Invoke(pass, firstRow, pendingBand);
                    cancellation.ThrowIfCancellationRequested();
                    if (pendingBand.GetWidth() != Views * cellPixels || pendingBand.GetHeight() != viewportRows * cellPixels)
                        throw new InvalidOperationException("Impostor row readback dimensions do not match its viewport.");
                    // Finish converted the old complete readback to RGBA8. Converting each pixel before the
                    // lossless copy is the same operation; no per-band dilation, mipmaps or alpha adjustment.
                    pendingBand.Convert(Image.Format.Rgba8);
                    pendingReadback.BlitRect(pendingBand, new Rect2I(Vector2I.Zero, pendingBand.GetSize()),
                        new Vector2I(0, firstRow * cellPixels));
                    pendingBand.Dispose(); pendingBand = null;
                    TraceAtlas(trace, "band-copied", pass, Views * cellPixels, viewportRows * cellPixels);
                }
                fixture?.Readback?.Invoke(pass, pendingReadback);
                TraceAtlas(trace, "readback", pass, pendingReadback.GetWidth(), pendingReadback.GetHeight());
                int p = pass;
                string dump = OS.GetEnvironment("HU_FLORA_DUMP");
                if (pass == 1)
                {
                    // The copied normal readback is the viewport's last consumer. Wait for actual native
                    // destruction before starting final CPU work; QueueFree alone would overlap both lifetimes.
                    await RetireViewport(viewport, cancellation, fixture?.ViewportRetiring, fixture?.RetirementWaitArmed);
                    RenderMemoryTrace.Mark("flora-atlas:viewport-retired");
                }
                // The atlas's own work (dilation, mipmaps: seconds for thousands of texels a side) runs on a worker, so the
                // frames go on (the loading screen draws through it, GROUND.md §4.3.1); only the upload is the main thread's.
                // Finish owns this isolated CPU Image after Task.Run accepts it, even if already cancelled.
                fixture?.ProcessingStarting?.Invoke(pass);
                Task<Image> processing = ProcessAtlas(pendingReadback, p, rows, dump, cancellation, trace, fixture);
                pendingReadback = null;
                using Image atlas = await processing;
                cancellation.ThrowIfCancellationRequested();
                export?.Invoke(pass, atlas);
                cancellation.ThrowIfCancellationRequested();
                if (pass == 0)
                {
                    result.Colour = ImageTexture.CreateFromImage(atlas);
                }
                else
                {
                    result.Normal = ImageTexture.CreateFromImage(atlas);
                }
                fixture?.Uploaded?.Invoke(pass, pass == 0 ? result.Colour : result.Normal);
                TraceAtlas(trace, "uploaded", pass, atlas.GetWidth(), atlas.GetHeight());
            }
            _ownedResult = result;
            success = true;
            return result;
        }
        finally
        {
            pendingBand?.Dispose();
            pendingReadback?.Dispose();
            if (!success) result?.Dispose();
            if (viewport != null && GodotObject.IsInstanceValid(viewport)) viewport.QueueFree();
            _completed = true;
            _cancellation.Dispose();
            _stopped.TrySetResult(true);
        }
    }

    // Main-thread retirement. Native validity is checked after each asynchronous frame notification:
    // neither a TreeExited signal nor QueueFree returning proves the queued destruction has completed.
    // Cancellation wakes the existing frame wait even if rendering stops; BakeCore still owns its readback.
    private static async Task RetireViewport(SubViewport viewport, CancellationToken cancellation,
        Action<SubViewport>? queued = null, Action? waitArmed = null)
    {
        if (GodotObject.IsInstanceValid(viewport)) viewport.QueueFree();
        queued?.Invoke(viewport);
        while (GodotObject.IsInstanceValid(viewport))
        {
            cancellation.ThrowIfCancellationRequested();
            await NextDraw(cancellation, waitArmed);
        }
        cancellation.ThrowIfCancellationRequested();
    }

    // Cancellation wakes the await even when the renderer produces no more frames. Disconnect on the main thread.
    private static async Task NextDraw(CancellationToken cancellation, Action? armed = null)
    {
        var frame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Drawn() => frame.TrySetResult(true);
        RenderingServer.FramePostDraw += Drawn;
        using CancellationTokenRegistration registration = cancellation.Register(static state =>
            ((TaskCompletionSource<bool>)state!).TrySetCanceled(), frame);
        try { armed?.Invoke(); await frame.Task; }
        finally { RenderingServer.FramePostDraw -= Drawn; }
    }

    // A pass's read-back atlas made ready to draw: dilated into its transparent texels, with mipmaps (the colour pass's
    // keep each cell's share of leaf texels, so far foliage doesn't thin).
    private static Task<Image> ProcessAtlas(Image image, int pass, int rows, string dump,
        CancellationToken cancellation, bool trace, OwnershipFixtureHooks? fixture) =>
        Task.Run(() => Finish(image, pass, rows, dump, cancellation, trace, fixture));

    private static Image Finish(Image image, int pass, int rows, string dump, CancellationToken cancellation, bool trace,
        OwnershipFixtureHooks? fixture = null)
    {
        int w, h;
        byte[] rgba;
        using (image)
        {
            cancellation.ThrowIfCancellationRequested();
            image.Convert(Image.Format.Rgba8);
            if (dump.Length > 0)
            {
                image.SavePng(dump + $"/impostor_{pass}.png");
            }
            w = image.GetWidth(); h = image.GetHeight();
            fixture?.BeforeGetData?.Invoke(pass, image);
            rgba = image.GetData();
        }
        // Only owned bytes are needed from here. The input Image is already disposed, including when
        // conversion, optional dump or GetData throws; it does not overlap dilation or mip generation.
        TraceAtlas(trace, "readback-released", pass, w, h);
        fixture?.BeforePixels?.Invoke(pass, image);
        cancellation.ThrowIfCancellationRequested();
        TraceAtlas(trace, "finish-begin", pass, w, h);
        byte[] pixels = FloraTextures.ImpostorPixels(rgba, w, h, Views, rows, pass == 0, cancellation);
        Image atlas = Image.CreateFromData(w, h, pass == 0, Image.Format.Rgba8, pixels);
        try
        {
            if (pass != 0) atlas.GenerateMipmaps();
            cancellation.ThrowIfCancellationRequested();
            TraceAtlas(trace, "finish-end", pass, w, h);
            return atlas;
        }
        catch { atlas.Dispose(); throw; }
    }

    // Tiny asset-free fixture uses the production input-ownership and cancellation path, without a viewport.
    internal static Image FinishLifecycleFixture(Image image, int pass, int rows, CancellationToken cancellation = default,
        OwnershipFixtureHooks? fixture = null) =>
        Finish(image, pass, rows, "", cancellation, false, fixture);

    // Diagnostics are opt-in and outside all pixel loops. Process values include concurrent startup work;
    // payload estimates describe these arrays only, not native image/viewport memory or GPU residency.
    private static void TraceAtlas(bool enabled, string phase, int pass, int width, int height)
    {
        if (!enabled) return;
        try
        {
            using Process process = Process.GetCurrentProcess();
            long baseBytes = (long)width * height * 4;
            int mw = Math.Max(1, width / 2), mh = Math.Max(1, height / 2);
            long mipWork = pass == 0 ? ((long)mw * mh + (long)Math.Max(1, mw / 2) * Math.Max(1, mh / 2)) * 16 : 0;
            GD.Print("[flora-atlas] " + JsonSerializer.Serialize(new
            {
                phase, pass, width, height, processSeconds = Time.GetTicksUsec() / 1_000_000.0,
                workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64,
                managedBytes = GC.GetTotalMemory(false), processTotalAllocatedBytes = GC.GetTotalAllocatedBytes(false),
                inputRgbaBytes = baseBytes, legacyFloatBaseBytes = baseBytes * 4, legacyDilationCopyBytes = baseBytes * 4,
                bandFloatBytes = (long)width * Math.Min(height, FloraTextures.ImpostorBandRows + 8) * 16,
                dilationRowBytes = (long)width * 3 * 16, mipWorkFloatBytes = mipWork,
                packedOutputBytes = pass == 0 ? FloraTextures.MipByteCount(width, height) : baseBytes,
            }));
        }
        catch (Exception error) { GD.Print($"[flora-atlas] phase={phase}; diagnostics unavailable: {error.GetType().Name}"); }
    }
}
