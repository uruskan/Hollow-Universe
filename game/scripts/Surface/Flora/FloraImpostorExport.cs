using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface.Flora;

/// <summary>
/// One-process asset diagnostic using the ordinary library and baker. Its raw finished mip bytes are evidence,
/// not an admitted runtime cache. No planet, character, save or terrain texture library is loaded.
/// </summary>
public partial class FloraImpostorExport : Node
{
    public string OutputDirectory { get; set; } = "";
    public int LibrarySeed { get; set; }
    private ImpostorBaker? _baker;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<object> _passes = new();
    private readonly List<object> _nearImages = new();
    private readonly Dictionary<string, FloraAppearanceImageHash> _imageHashes = new(StringComparer.Ordinal);
    // Neutral globals and tiny textures outlive the captured library and retire last.
    private readonly List<Resource> _sourceInputs = new();
    private SurfaceDiagnosticGlobals? _sourceGlobals;
    private string _phase = "admission";

    public override void _Process(double delta)
    {
        if (_clock.Elapsed.TotalSeconds > 150) _baker?.Stop();
    }

    public override async void _Ready()
    {
        int exitCode = 1;
        bool admitted = false;
        FloraLibrary.Data? data = null;
        FloraLibrary? library = null;
        ImpostorBaker.Result? result = null;
        FloraAppearanceInputs? inputs = null;
        IDisposable? failedCreation = null;
        ImageTextureLayered? groundAlbedo = null, groundNormal = null;
        try
        {
            OutputDirectory = Path.GetFullPath(OutputDirectory);
            if (Directory.Exists(OutputDirectory) || File.Exists(OutputDirectory))
                throw new InvalidOperationException("Impostor export requires a new directory.");
            Directory.CreateDirectory(OutputDirectory);
            admitted = true;
            _phase = "procedural inputs";
            data = await Task.Run(() => FloraLibrary.Build(LibrarySeed));
            _phase = "export exact near images";
            _nearImages.Add(ExportImage("near-atlas", data.Atlas));
            _nearImages.Add(ExportImage("bark", data.Bark));
            _nearImages.Add(ExportImage("bark-normal", data.BarkNormal));
            _nearImages.Add(ExportImage("birch", data.Birch));
            _nearImages.Add(ExportImage("birch-normal", data.BirchNormal));
            var bladeMean = new[] { data.BladeMean.X, data.BladeMean.Y, data.BladeMean.Z };
            _phase = "native library";
            SetupSourceGlobals();
            // These arrays are bound only to the rock material, which Bake filters out. Tree resources,
            // row order, geometry, material inputs and processing use the complete production Create path.
            groundAlbedo = ArrayTexture(Colors.White);
            groundNormal = ArrayTexture(new Color(0.5f, 0.5f, 1));
            inputs = new FloraAppearanceInputs(new FloraAppearanceInputLimits(1048576, 268435456,
                1073741824, 33554432, 262144, 1048576));
            library = FloraLibrary.Create(data, groundAlbedo, groundNormal, inputs);
            ReleaseData(data); data = null;
            var rows = new List<object>();
            int rowIndex = 0;
            foreach (FloraLibrary.Species species in library.All.Where(s => s.Tree))
            for (int variantIndex = 0; variantIndex < species.Variants.Count; variantIndex++)
            {
                FloraLibrary.Variant variant = species.Variants[variantIndex];
                for (int surface = 0; surface < variant.Lod0.GetSurfaceCount(); surface++)
                    if (variant.Lod0.SurfaceGetMaterial(surface) == library.Rock)
                        throw new InvalidOperationException("A tree unexpectedly depends on terrain texture inputs.");
                rows.Add(new { row = rowIndex++, species = species.Name, kind = (int)species.Kind,
                    variant = variantIndex, height = variant.Height, extent = variant.Extent });
            }
            if (rows.Count is < 1 or > 32) throw new InvalidOperationException("Export row bound exceeded.");
            _baker = new ImpostorBaker { Name = "IsolatedImpostorExport" };
            AddChild(_baker);
            _phase = "ordinary bake";
            await _baker.BakeForExport(library, ExportPass);
            result = _baker.TakeResult();
            if (_passes.Count != 2 || result.Rows != rows.Count)
                throw new InvalidOperationException("Incomplete export or inconsistent row binding.");
            if (_imageHashes.Count != FloraAppearanceSchema.Images.Count)
                throw new InvalidOperationException("Incomplete source/impostor image set.");
            foreach (FloraAppearanceImageLayout layout in FloraAppearanceSchema.Images)
                File.Move(Path.Combine(OutputDirectory, layout.File + ".pending"), Path.Combine(OutputDirectory, layout.File));
            _phase = "capture exact creation inputs";
            FloraAppearanceInputContract captured = inputs.CaptureExport(library, LibrarySeed,
                Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..")));
            byte[] manifest = FloraAppearanceManifest.Encode("earth-legacy", 1, captured,
                new FloraAppearanceColour(bladeMean[0], bladeMean[1], bladeMean[2]),
                FloraAppearanceSchema.Images.Select(layout => _imageHashes[layout.Role]).ToArray());
            string manifestHash = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
            string manifestPath = Path.Combine(OutputDirectory, FloraAppearanceManifest.FileName);
            using (var file = new FileStream(manifestPath + ".pending", FileMode.CreateNew,
                System.IO.FileAccess.Write, FileShare.None)) file.Write(manifest);
            using (var file = File.OpenRead(manifestPath + ".pending"))
                if (Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant() != manifestHash)
                    throw new IOException("Written appearance manifest did not roundtrip exactly.");
            File.Move(manifestPath + ".pending", manifestPath);
            _phase = "complete";
            File.WriteAllText(Path.Combine(OutputDirectory, "export.json"), JsonSerializer.Serialize(new
            {
                schema = "flora-impostor-evidence-v1", completed = true,
                runtimeCacheAdmission = false,
                admissionNote = "Finished pixels and captured creation inputs; independent pin review and runtime validation remain required.",
                manifestSha256 = manifestHash, inputFingerprint = captured.Fingerprint,
                runtimeFingerprint = FloraAppearanceInputs.RuntimeFingerprint(captured),
                librarySeed = LibrarySeed, engine = Engine.GetVersionInfo()["string"].AsString(),
                renderer = RenderingServer.GetCurrentRenderingMethod(),
                device = RenderingServer.GetVideoAdapterName(), rows,
                nativeRows = new[] { result.NativeRows.X, result.NativeRows.Y },
                passes = _passes, nearImages = _nearImages, bladeMean, seconds = _clock.Elapsed.TotalSeconds,
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[flora-impostor-export] complete; rows={rows.Count}; {OutputDirectory}");
            exitCode = 0;
        }
        catch (Exception error)
        {
            if (error is FloraLibrary.CreationCleanupException creation) failedCreation = creation.PendingCleanup;
            GD.PushError($"Impostor export failed in {_phase}: {error}");
            if (admitted) File.WriteAllText(Path.Combine(OutputDirectory, "incomplete.json"),
                JsonSerializer.Serialize(new { completed = false, phase = _phase, error = error.ToString(),
                    passes = _passes, seconds = _clock.Elapsed.TotalSeconds }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            var cleanupErrors = new List<string>();
            // A failed owner must not prevent independent owners being attempted. Keep retry handles until
            // this isolated process exits, and do not report a successful export after incomplete retirement.
            var pending = new List<IDisposable>();
            if (_baker != null)
            {
                try
                {
                    _baker.Stop();
                    // A cleanup exception inside the baker may prevent its completion signal. This
                    // isolated exporter must still exit with its dependent native owners retained.
                    Task stopped = _baker.Stopped;
                    if (await Task.WhenAny(stopped, Task.Delay(TimeSpan.FromSeconds(5))) != stopped)
                        throw new TimeoutException("Baker did not acknowledge retirement within five seconds.");
                    await stopped;
                    _baker.Free(); _baker = null;
                }
                catch (Exception error) { cleanupErrors.Add("baker: " + error); }
            }
            if (result != null) pending.Add(result);
            if (library != null) pending.Add(library);
            if (failedCreation != null) pending.Add(failedCreation);
            // If the baker did not stop, its inputs must remain alive until process shutdown.
            if (_baker == null)
            {
                for (int attempt = 0; attempt < 2 && pending.Count != 0; attempt++)
                    for (int i = pending.Count - 1; i >= 0; i--)
                        try { pending[i].Dispose(); pending.RemoveAt(i); }
                        catch (Exception error) { cleanupErrors.Add("native owner: " + error); }
            }
            else cleanupErrors.Add("Baker retained its source owners until process shutdown.");
            TryCleanup("input receipts", () => inputs?.Dispose());
            if (data != null)
            {
                TryCleanup("near atlas", data.Atlas.Dispose); TryCleanup("bark", data.Bark.Dispose);
                TryCleanup("bark normal", data.BarkNormal.Dispose); TryCleanup("birch", data.Birch.Dispose);
                TryCleanup("birch normal", data.BirchNormal.Dispose); data.Species.Clear();
            }
            if (pending.Count == 0 && _baker == null)
            {
                TryCleanup("ground albedo", () => groundAlbedo?.Dispose());
                TryCleanup("ground normal", () => groundNormal?.Dispose());
                TryCleanup("source globals", () => _sourceGlobals?.Dispose());
                for (int i = _sourceInputs.Count - 1; i >= 0; i--)
                    TryCleanup("source texture", _sourceInputs[i].Dispose);
            }
            if (cleanupErrors.Count != 0)
            {
                exitCode = 1;
                GD.PushError("Impostor export cleanup: " + string.Join("\n", cleanupErrors));
                if (admitted) TryCleanup("cleanup report", () => File.WriteAllText(
                    Path.Combine(OutputDirectory, "cleanup-incomplete.json"), JsonSerializer.Serialize(new
                    { completed = false, pendingOwners = pending.Count, errors = cleanupErrors.ToArray() })));
            }
            GetTree().Quit(exitCode);

            void TryCleanup(string owner, Action action)
            {
                try { action(); }
                catch (Exception error) { cleanupErrors.Add(owner + ": " + error); }
            }
        }
    }

    private void ExportPass(int pass, Image image)
    {
        _phase = $"export finished pass {pass}";
        if (pass != _passes.Count || pass > 1 || image.GetFormat() != Image.Format.Rgba8 || !image.HasMipmaps()
            || image.GetWidth() != ImpostorBaker.Views * ImpostorBaker.CellPixels
            || image.GetHeight() < ImpostorBaker.CellPixels || image.GetHeight() > 32 * ImpostorBaker.CellPixels)
            throw new InvalidOperationException("Unexpected finished image layout.");
        _passes.Add(ExportImage(pass == 0 ? "impostor-colour" : "impostor-normal", image));
    }

    private object ExportImage(string role, Image image)
    {
        FloraAppearanceImageLayout layout = FloraAppearanceSchema.Images.Single(i => i.Role == role);
        if (image.GetFormat() != Image.Format.Rgba8 || image.GetWidth() != layout.Width ||
            image.GetHeight() != layout.Height || image.GetMipmapCount() + 1 != layout.MipLevels)
            throw new InvalidOperationException("Source image differs from the exact appearance layout: " + role);
        byte[] bytes = image.GetData();
        if (bytes.Length != layout.ByteLength) throw new InvalidOperationException("Finished mip byte count mismatch: " + role);
        string path = Path.Combine(OutputDirectory, layout.File + ".pending");
        using (var file = new FileStream(path, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None)) file.Write(bytes);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using (var file = File.OpenRead(path))
            if (Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant() != hash)
                throw new IOException("Written image bytes did not roundtrip exactly.");
        if (image.SavePng(Path.Combine(OutputDirectory, role + "-review.png")) != Error.Ok)
            throw new IOException("Review image could not be saved.");
        _imageHashes.Add(role, new FloraAppearanceImageHash(role, hash));
        return new { role, file = layout.File, sha256 = hash, bytes = bytes.Length,
            width = image.GetWidth(), height = image.GetHeight(), format = "RGBA8",
            mipmaps = image.GetMipmapCount(), savedBytesEqualFinishedImage = true };
    }

    private static ImageTextureLayered ArrayTexture(Color colour)
    {
        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(colour);
        var images = new Godot.Collections.Array<Image> { image };
        var texture = new Texture2DArray();
        if (texture.CreateFromImages(images) != Error.Ok) { texture.Dispose(); throw new InvalidOperationException("Rock-only placeholder array failed."); }
        return texture;
    }

    private void SetupSourceGlobals()
    {
        PlanetGlobals.Register();
        _sourceGlobals = new SurfaceDiagnosticGlobals();
        _sourceGlobals.Setup(PlanetGlobals.Radius, Solid(Colors.Black), Solid(Colors.White),
            Solid(new Color(0.7f, 0.7f, 0.7f)), Volume(Colors.Black), Volume(Colors.White));

        ImageTexture Solid(Color colour)
        {
            using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            image.Fill(colour);
            ImageTexture texture = ImageTexture.CreateFromImage(image);
            _sourceInputs.Add(texture);
            return texture;
        }

        ImageTexture3D Volume(Color colour)
        {
            using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            image.Fill(colour);
            var images = new Godot.Collections.Array<Image> { image };
            var texture = new ImageTexture3D();
            if (texture.Create(Image.Format.Rgba8, 1, 1, 1, false, images) != Error.Ok)
            { texture.Dispose(); throw new InvalidOperationException("Diagnostic air input failed."); }
            _sourceInputs.Add(texture);
            return texture;
        }
    }

    private static void ReleaseData(FloraLibrary.Data data)
    {
        data.Atlas.Dispose(); data.Bark.Dispose(); data.BarkNormal.Dispose();
        data.Birch.Dispose(); data.BirchNormal.Dispose(); data.Species.Clear();
    }
}
