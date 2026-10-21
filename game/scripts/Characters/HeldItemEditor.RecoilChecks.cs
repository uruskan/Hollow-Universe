using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.Ground;
using HollowUniverse.Items;

namespace HollowUniverse.Characters;

public partial class HeldItemEditor
{
    private sealed record RecoilCheckSample(string Mode, string State, int Frame, double Seconds, string Action,
        float Phase, float[] Position, float[] RotationDegrees, float PrimaryGripMetres, float SecondaryGripMetres,
        float PalmMetres, float ReachCorrectionMetres, float ShoulderAccommodationMetres,
        float AttachmentPositionErrorMetres, float AttachmentBasisError, float? OpticalCameraDegrees,
        float? OpticalProjectionError);

    /// <summary>Exercise the gameplay fire clock at 450 RPM, including shots that overlap recovery.
    /// The workshop controller is stopped, but the real held renderer is stepped and actually drawn.</summary>
    private async void RunRecoilChecks()
    {
        const int frequency = 120, shotFrames = 16, burstFrames = 192, settleFrames = 144;
        const int tileWidth = 320, tileHeight = 180, columns = 6;
        const float gripLimit = 0.025f, triggerMetresLimit = 0.00002f, triggerDegreesLimit = 0.01f;
        const float stepMetresLimit = 0.03f, stepDegreesLimit = 4f;
        int previewWidth = Math.Clamp((int)Number("recoil-check-width", 1280), 720, 1920);
        const int previewHeight = 720;
        string output = Option("recoil-check-out", Path.Combine(ProjectSettings.GlobalizePath("user://"),
            "held-recoil-check-" + Guid.NewGuid().ToString("N")));
        bool reportOnly = _options.ContainsKey("recoil-check-report-only"), outputReady = false;
        var violations = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();
        var samples = new List<RecoilCheckSample>();
        var triggers = new List<object>();
        var captures = new List<object>();
        var modeMetrics = new List<object>();
        float maxTriggerMetres = 0, maxTriggerDegrees = 0, maxStepMetres = 0, maxStepDegrees = 0;
        float originalFov = CharacterMaterials.ViewmodelFov;
        string fingerprint = DiskFingerprint();
        void SaveReport() => File.WriteAllText(Path.Combine(output, "recoil-check.json"), JsonSerializer.Serialize(new
        {
            status = failures.Count > 0 ? "fail" : reportOnly ? "report" : violations.Count == 0 ? "pass" : "fail",
            reportOnly, sourceFingerprint = fingerprint, simulationHz = frequency, roundsPerMinute = 450,
            body = _body.ToString(), width = previewWidth, height = previewHeight,
            burstSeconds = (double)burstFrames / frequency, settleSeconds = (double)settleFrames / frequency,
            limits = new { gripMetres = gripLimit, triggerMetres = triggerMetresLimit, triggerDegrees = triggerDegreesLimit,
                stepMetresAt120Hz = stepMetresLimit, stepDegreesAt120Hz = stepDegreesLimit },
            note = "Native viewmodel presentation check with exact gameplay Trigger(Fire) calls, 12 overlapping shots per mode, and actual rendered evidence. It does not simulate projectile hits or assert subjective visual quality. Trigger deltas include a zero-time pose evaluation.",
            maxTriggerMetres, maxTriggerDegrees, maxStepMetres, maxStepDegrees,
            failures, violations, modeMetrics, triggers, captures, samples,
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        try
        {
            output = Path.GetFullPath(output);
            Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(),
                "Recoil output must be a new or empty directory: " + output);
            Directory.CreateDirectory(output); outputReady = true;
            SetProcess(false); SetPlaying(false); _contactPreview = false; _showTarget = false; _showGizmo = false;
            UpdateGizmo(); SetView(0); _previewContainer.Stretch = false; _preview.Size = new Vector2I(previewWidth, previewHeight);
            _captureCaption.Visible = true; _captureCaption.AddThemeFontSizeOverride("font_size", Ui.Px(18));
            CharacterMaterials.SetViewmodelFov(70);
            HeldItemModel rifle = HeldItems.Find("rifle") ?? throw new InvalidOperationException("Frontier AK model is missing.");
            var definition = _content.ItemByKey("rifle").Firearm ?? throw new InvalidOperationException("Frontier AK gun definition is missing.");
            Require(Math.Abs(definition.IntervalSeconds - (double)shotFrames / frequency) < 0.0001,
                "Recoil fixture's 450 RPM cadence no longer matches Frontier AK gameplay data.");
            Node3D fixture = RecoilFixture(out MeshInstance3D[] markers);
            string[] modes = { "hip", "iron", "holo", "scope" };
            using Image sheet = Image.CreateEmpty(tileWidth * columns, tileHeight * modes.Length, false, Image.Format.Rgba8);
            sheet.Fill(new Color(0.025f, 0.035f, 0.05f));
            for (int row = 0; row < modes.Length; row++)
            {
                string mode = modes[row]; _aiming = mode != "hip";
                BuildCharacters(); _viewmodel.SetProcess(false); _character.SetProcess(false);
                fixture.Position = new Vector3(0, _eyes.Position.Y, -8);
                SelectItem(rifle); SelectAction("idle"); SetView(0); _viewmodel.ClearPreview();
                _viewmodel.Apply(new ViewmodelFrame { Grounded = true, Aiming = _aiming });
                Node3D gun = _viewmodel.GetNode<Node3D>("rifle");
                string? attachmentKey = mode == "holo" ? "holo-sight" : mode == "scope" ? "scope" : null;
                byte mods = attachmentKey == null ? (byte)0 : (byte)(1 << _content.ItemByKey(attachmentKey).Attachment!.Index);
                GunEffects.Dress(_content, gun, "rifle", mods, ViewmodelView.PrepareItem, firstPerson: true);
                float zoom = !_aiming ? 1 : (float)(attachmentKey == null ? definition.Zoom : _content.ItemByKey(attachmentKey).Attachment!.Zoom);
                _camera.Fov = Mathf.RadToDeg(2 * MathF.Atan(MathF.Tan(Mathf.DegToRad(70) * 0.5f) / zoom));
                for (int frame = 0; frame < frequency * 2; frame++) _viewmodel._Process(1d / frequency);
                Node3D? sight = gun.FindChildren("sight", "Node3D", true, false).OfType<Node3D>().FirstOrDefault(n => n.HasMeta("attachment_key"));
                Require(attachmentKey == null || sight != null, mode + ": fitted optic is missing.");
                ScopeLensView? scope = sight?.GetNodeOrNull<ScopeLensView>("ScopeImage");
                MeshInstance3D? lens = sight?.FindChildren("OpticLens_lod0", "MeshInstance3D", true, false).OfType<MeshInstance3D>().FirstOrDefault();
                Require(attachmentKey == null || lens != null, mode + ": actual optic aperture is missing.");
                Transform3D? sightRest = sight == null ? null : gun.GlobalTransform.AffineInverse() * sight.GlobalTransform;
                Transform3D? lensRest = lens == null ? null : gun.GlobalTransform.AffineInverse() * lens.GlobalTransform;
                Transform3D idle = gun.Transform;
                int captureColumn = 0, frameNumber = 0, modeShots = 0, renderedDifferences = 0;
                float modeTriggerMetres = 0, modeTriggerDegrees = 0, modeStepMetres = 0, modeStepDegrees = 0, modeGripMetres = 0;
                int maximumStepFrame = 0;
                Image? idleImage = null;
                RecoilCheckSample Measure(string state)
                {
                    scope?._Process(0);
                    CheckFinite(_viewmodel, mode + "/" + state);
                    float attachmentMetres = 0, attachmentBasis = 0;
                    void CheckRigid(Node3D? part, Transform3D? rest)
                    {
                        if (part == null || rest == null) return;
                        Transform3D actual = gun.GlobalTransform.AffineInverse() * part.GlobalTransform;
                        attachmentMetres = Math.Max(attachmentMetres, actual.Origin.DistanceTo(rest.Value.Origin));
                        attachmentBasis = Math.Max(attachmentBasis, LocomotionBasisError(actual.Basis, rest.Value.Basis));
                    }
                    CheckRigid(sight, sightRest); CheckRigid(lens, lensRest);
                    float? opticalDegrees = null, projectionError = null;
                    if (scope != null)
                    {
                        Require(scope.Rendering, mode + ": the scope world viewport must render during ADS.");
                        opticalDegrees = RecoilDegrees(_camera.GlobalBasis, scope.OpticalCamera.GlobalBasis);
                        if (opticalDegrees > 0.01f || scope.OpticalCamera.GlobalPosition.DistanceTo(_camera.GlobalPosition) > 0.00001f)
                            violations.Add(mode + ": optical camera must share the aiming camera transform throughout recoil.");
                        ShaderMaterial material = (ShaderMaterial)lens!.GetActiveMaterial(0);
                        Variant crop = material.GetShaderParameter("optical_image_scale");
                        if (crop.VariantType != Variant.Type.Nil && crop.AsSingle() > 0)
                        {
                            projectionError = MathF.Abs(MathF.Tan(Mathf.DegToRad(scope.OpticalCamera.Fov) * 0.5f) / crop.AsSingle() -
                                MathF.Tan(Mathf.DegToRad(_camera.Fov) * 0.5f));
                            if (projectionError > 0.0001f) violations.Add(mode + ": optical world scale must match the main camera.");
                        }
                        else violations.Add(mode + ": optical image must expose a valid camera-centred projection scale.");
                    }
                    float primary = _viewmodel.PrimaryHandAttachmentError, secondary = _viewmodel.SecondaryHandAttachmentError;
                    float palm = _viewmodel.PrimaryPalmContactError;
                    modeGripMetres = Math.Max(modeGripMetres, Math.Max(palm, Math.Max(primary, secondary)));
                    Require(float.IsFinite(primary) && float.IsFinite(secondary) && float.IsFinite(palm), mode + ": nonfinite grip metric.");
                    if (primary > gripLimit || secondary > gripLimit || palm > gripLimit)
                        violations.Add(mode + ": both hands must remain attached within 25 mm throughout the burst.");
                    if (attachmentMetres > 0.00002f || attachmentBasis > 0.00002f)
                        violations.Add(mode + ": scope body and glass must remain rigidly attached to the weapon.");
                    Vector3 p = gun.Position, e = gun.Basis.GetEuler() * (180f / MathF.PI);
                    var sample = new RecoilCheckSample(mode, state, frameNumber, (double)frameNumber / frequency,
                        _viewmodel.ActiveAction, _viewmodel.ActivePhase, new[] { p.X, p.Y, p.Z }, new[] { e.X, e.Y, e.Z },
                        primary, secondary, palm, _viewmodel.AuthoredReachCorrection, _viewmodel.OpticShoulderAccommodation,
                        attachmentMetres, attachmentBasis, opticalDegrees, projectionError);
                    samples.Add(sample); return sample;
                }
                async Task Capture(string label)
                {
                    RecoilCheckSample sample = Measure(label);
                    _captureCaption.Text = $"Frontier AK · {mode} · {label} · {frameNumber / (float)frequency:0.000}s\n" +
                        $"450 RPM · shot {modeShots} · grip {sample.PrimaryGripMetres * 1000:0.0}/{sample.SecondaryGripMetres * 1000:0.0} mm";
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using Image image = _preview.GetTexture().GetImage();
                    Require(!image.IsEmpty() && image.GetWidth() == previewWidth && image.GetHeight() == previewHeight, mode + ": no valid rendered frame.");
                    image.Convert(Image.Format.Rgba8);
                    float energy = 0, minimum = float.PositiveInfinity, maximum = 0;
                    for (int y = 80; y < image.GetHeight(); y += 16)
                    for (int x = 0; x < image.GetWidth(); x += 16)
                    {
                        Color c = image.GetPixel(x, y); float light = c.R + c.G + c.B;
                        energy += light; minimum = Math.Min(minimum, light); maximum = Math.Max(maximum, light);
                    }
                    Require(float.IsFinite(energy) && energy > 1 && maximum - minimum > 0.1f, mode + ": rendered evidence is blank or invalid.");
                    int changed = 0;
                    if (idleImage != null)
                    {
                        for (int y = 100; y < image.GetHeight(); y += 4)
                        for (int x = 0; x < image.GetWidth(); x += 4)
                        {
                            Color a = image.GetPixel(x, y), b = idleImage.GetPixel(x, y);
                            if (MathF.Abs(a.R - b.R) + MathF.Abs(a.G - b.G) + MathF.Abs(a.B - b.B) > 0.08f) changed++;
                        }
                        if (label != "settled") renderedDifferences += changed;
                    }
                    else idleImage = (Image)image.Duplicate();
                    bool? centredReticle = lens == null ? null : RecoilRedAtCentre(image);
                    if (centredReticle == false) violations.Add(mode + ": rendered reticle must cover the screen centre in every captured ADS state.");
                    var markerChecks = new List<object>();
                    if (scope != null)
                    {
                        using Image optical = scope.OpticalViewport.GetTexture().GetImage();
                        Require(!optical.IsEmpty() && optical.GetWidth() == ScopeLensView.Resolution, mode + ": no actual optical world render.");
                        for (int marker = 0; marker < markers.Length; marker++)
                        {
                            Vector2 worldPixel = _camera.UnprojectPosition(markers[marker].GlobalPosition);
                            Vector2 opticalPixel = scope.OpticalCamera.UnprojectPosition(markers[marker].GlobalPosition);
                            bool mainMatch = RecoilMarkerAt(image, worldPixel, marker), opticalMatch = RecoilMarkerAt(optical, opticalPixel, marker);
                            markerChecks.Add(new { marker, worldPixel = new[] { worldPixel.X, worldPixel.Y },
                                opticalPixel = new[] { opticalPixel.X, opticalPixel.Y }, mainMatch, opticalMatch });
                            if (!mainMatch || !opticalMatch)
                                violations.Add(mode + ": colored world markers must register at the aiming camera's projected positions through the rendered lens.");
                        }
                        Require(optical.SavePng(Path.Combine(output, mode + "-" + label + "-optical.png")) == Error.Ok,
                            mode + ": could not save optical render evidence.");
                    }
                    string file = mode + "-" + label + ".png";
                    Require(image.SavePng(Path.Combine(output, file)) == Error.Ok, "Could not save recoil frame " + file);
                    captures.Add(new { mode, label, frame = frameNumber, file, changedSamplesSinceIdle = changed, centredReticle, markerChecks });
                    image.Resize(tileWidth, tileHeight, Image.Interpolation.Lanczos);
                    sheet.BlitRect(image, new Rect2I(0, 0, tileWidth, tileHeight), new Vector2I(captureColumn++ * tileWidth, row * tileHeight));
                }
                await Capture("idle");
                for (int frame = 0; frame < burstFrames + settleFrames; frame++)
                {
                    frameNumber = frame;
                    if (frame < burstFrames && frame % shotFrames == 0)
                    {
                        if (frame == shotFrames) await Capture("before-second-shot");
                        Transform3D before = gun.Transform;
                        _viewmodel.Trigger(CharacterEvent.Fire); _viewmodel._Process(0); modeShots++;
                        float jumpMetres = before.Origin.DistanceTo(gun.Position), jumpDegrees = RecoilDegrees(before.Basis, gun.Basis);
                        maxTriggerMetres = Math.Max(maxTriggerMetres, jumpMetres); maxTriggerDegrees = Math.Max(maxTriggerDegrees, jumpDegrees);
                        modeTriggerMetres = Math.Max(modeTriggerMetres, jumpMetres); modeTriggerDegrees = Math.Max(modeTriggerDegrees, jumpDegrees);
                        triggers.Add(new { mode, shot = modeShots, frame, seconds = (double)frame / frequency, jumpMetres, jumpDegrees });
                        if (jumpMetres > triggerMetresLimit || jumpDegrees > triggerDegreesLimit)
                            violations.Add(mode + ": a new shot must not teleport the rendered weapon during unfinished recovery.");
                        Measure("trigger");
                        if (frame == shotFrames) await Capture("after-second-shot");
                    }
                    Transform3D previous = gun.Transform;
                    _viewmodel._Process(1d / frequency); frameNumber = frame + 1;
                    float stepMetres = previous.Origin.DistanceTo(gun.Position), stepDegrees = RecoilDegrees(previous.Basis, gun.Basis);
                    maxStepMetres = Math.Max(maxStepMetres, stepMetres); maxStepDegrees = Math.Max(maxStepDegrees, stepDegrees);
                    if (stepMetres > modeStepMetres) maximumStepFrame = frameNumber;
                    modeStepMetres = Math.Max(modeStepMetres, stepMetres); modeStepDegrees = Math.Max(modeStepDegrees, stepDegrees);
                    if (stepMetres > stepMetresLimit || stepDegrees > stepDegreesLimit)
                        violations.Add(mode + ": weapon motion must stay below 30 mm / 4 degrees per 120 Hz frame, including arm-reach corrections.");
                    Measure(frame < burstFrames ? "burst" : "recovery");
                    if (frameNumber == 5) await Capture("first-peak");
                    if (frameNumber == burstFrames - 8) await Capture("late-burst");
                }
                await Capture("settled"); idleImage?.Dispose();
                Require(modeShots == 12 && captureColumn == columns, mode + ": incomplete burst or rendered evidence.");
                Require(renderedDifferences > 100, mode + ": sampled firing poses did not produce a changing rendered weapon.");
                // Hip idle continues its authored breathing clock during the burst. Its peak-to-peak
                // translation is 3 mm; only this settled-position comparison allows that drift.
                if (gun.Position.DistanceTo(idle.Origin) > 0.003f || RecoilDegrees(idle.Basis, gun.Basis) > 0.05f)
                    violations.Add(mode + ": completed recovery must return to the initial hold.");
                modeMetrics.Add(new { mode, maxTriggerMetres = modeTriggerMetres, maxTriggerDegrees = modeTriggerDegrees,
                    maxStepMetres = modeStepMetres, maxStepDegrees = modeStepDegrees, maximumStepFrame, maxGripMetres = modeGripMetres });
            }
            Require(sheet.SavePng(Path.Combine(output, "recoil-contact-sheet.png")) == Error.Ok, "Could not save recoil contact sheet.");
            Require(fingerprint == DiskFingerprint(), "Recoil checks must not edit held presentation data.");
            SaveReport();
            Require(reportOnly || violations.Count == 0, string.Join("\n", violations));
            GD.Print($"[recoil-check] {(reportOnly ? "REPORT" : "PASS")}: {samples.Count} samples, {triggers.Count} live shots, {captures.Count} rendered frames, {violations.Count} violations; {output}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            failures.Add(error.Message); if (outputReady) SaveReport();
            GD.PushError("[recoil-check] FAIL: " + error); GetTree().Quit(1);
        }
        finally { CharacterMaterials.SetViewmodelFov(originalFov); }
    }

    private static float RecoilDegrees(Basis a, Basis b)
    {
        // Quaternion.AngleTo loses precision at tiny rotations; trace the relative orthonormal basis instead.
        Basis relative = a.Orthonormalized().Inverse() * b.Orthonormalized();
        float sine = new Vector3(relative.Z.Y - relative.Y.Z, relative.X.Z - relative.Z.X, relative.Y.X - relative.X.Y).Length() * 0.5f;
        float cosine = (relative.X.X + relative.Y.Y + relative.Z.Z - 1) * 0.5f;
        return Mathf.RadToDeg(MathF.Atan2(sine, Math.Clamp(cosine, -1, 1)));
    }

    private static bool RecoilRedAtCentre(Image image)
    {
        int x = image.GetWidth() / 2, y = image.GetHeight() / 2;
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            Color colour = image.GetPixel(x + dx, y + dy);
            if (colour.R > 0.15f && colour.R > colour.G * 1.6f && colour.R > colour.B * 1.6f) return true;
        }
        return false;
    }

    private Node3D RecoilFixture(out MeshInstance3D[] markers)
    {
        var fixture = new Node3D { Name = "RecoilOpticalRegistrationTarget" };
        _stage.AddChild(fixture);
        fixture.AddChild(Box(new Vector3(7, 4, 0.05f), new Vector3(0, 0, -0.04f), Material(new Color(0.13f, 0.16f, 0.19f), true)));
        var grid = Material(new Color(0.24f, 0.28f, 0.31f), true);
        for (int line = -12; line <= 12; line++)
        {
            fixture.AddChild(Box(new Vector3(0.006f, 4, 0.008f), new Vector3(line * 0.25f, 0, 0), grid));
            if (line is >= -8 and <= 8)
                fixture.AddChild(Box(new Vector3(7, 0.006f, 0.008f), new Vector3(0, line * 0.25f, 0), grid));
        }
        Color[] colours = { new(0.015f, 0.35f, 0.35f), new(0.015f, 0.35f, 0.015f), new(0.015f, 0.03f, 0.35f), new(0.35f, 0.35f, 0.015f) };
        markers = new MeshInstance3D[colours.Length];
        for (int i = 0; i < markers.Length; i++)
        {
            markers[i] = Box(new Vector3(0.075f, 0.075f, 0.012f), new Vector3(i % 2 == 0 ? -0.14f : 0.14f, i < 2 ? 0.14f : -0.14f, 0.015f), Material(colours[i], true));
            fixture.AddChild(markers[i]);
        }
        return fixture;
    }

    private static bool RecoilMarkerAt(Image image, Vector2 pixel, int marker)
    {
        // Use chroma rather than absolute radiance: the optical viewport is linear HDR, while the
        // displayed lens has passed through the main viewport's tone mapping and exposure.
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int x = (int)MathF.Round(pixel.X) + dx, y = (int)MathF.Round(pixel.Y) + dy;
            if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight()) continue;
            Color c = image.GetPixel(x, y);
            bool match = marker switch
            {
                0 => Math.Min(c.G, c.B) > Math.Max(0.06f, c.R * 1.35f),
                1 => c.G > Math.Max(0.06f, Math.Max(c.R, c.B) * 1.35f),
                2 => c.B > Math.Max(0.06f, Math.Max(c.R, c.G) * 1.35f),
                _ => Math.Min(c.R, c.G) > Math.Max(0.06f, c.B * 1.35f),
            };
            if (match) return true;
        }
        return false;
    }
}
