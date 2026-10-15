using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Simulation.Content;

namespace HollowUniverse.Characters;

public partial class HeldItemEditor
{
    /// <summary>
    /// Exercises the actual controls and runtime previews, then restores the in-memory catalog.
    /// Disk round-tripping is restricted to a new temporary file, never the production catalog.
    /// </summary>
    private async void RunEditorCheck()
    {
        var originals = HeldItems.All.ToDictionary(i => i.Id, i => HeldPresentations.Clone(HeldPresentations.Get(i.Id)), StringComparer.Ordinal);
        string temporary = Path.Combine(Path.GetTempPath(), "hollow-held-editor-" + Guid.NewGuid().ToString("N") + ".json");
        int samples = 0, actions = 0, result = 0, attachmentWarnings = 0;
        float maxAttachmentError = 0;
        string maxAttachmentContext = "";
        float maxReachCorrection = 0;
        string maxReachContext = "";
        try
        {
            SetPlaying(false);
            float originalX = _profile.FirstPerson.Position.X;
            if (_testPositionX == null) throw new InvalidOperationException("FPS X control was not built.");
            _testPositionX.Value = originalX + 0.013f;
            Require(MathF.Abs(HeldPresentations.Get(_held.Id).FirstPerson.Position.X - (originalX + 0.013f)) < 0.0001f, "Changing the actual FPS X control must update the live catalog.");
            await Settle(2);
            Require(_viewmodel.CurrentPresentation != null && MathF.Abs(_viewmodel.CurrentPresentation.FirstPerson.Position.X - (originalX + 0.013f)) < 0.0001f, "FPS renderer must see the edited profile.");
            Undo();
            Require(MathF.Abs(_profile.FirstPerson.Position.X - originalX) < 0.0001f, "Undo must restore the original pose.");
            Redo();
            Require(MathF.Abs(_profile.FirstPerson.Position.X - (originalX + 0.013f)) < 0.0001f, "Redo must restore the edited pose.");
            HeldPresentations.Save(temporary);
            _testPositionX.Value = originalX + 0.029f;
            HeldPresentations.Reload(temporary);
            Require(MathF.Abs(HeldPresentations.Get(_held.Id).FirstPerson.Position.X - (originalX + 0.013f)) < 0.0001f, "Validated atomic save/reload must retain the edited pose.");
            SelectItem(_held);
            int originalKeys = Clip.Keys.Count;
            float insertedAt = 0.413f;
            while (Clip.Keys.Any(k => MathF.Abs(k.At - insertedAt) < 0.001f)) insertedAt += 0.013f;
            SetPhase(insertedAt);
            UpsertKey();
            Require(Clip.Keys.Count == originalKeys + 1, "Add key must create a key at the playhead.");
            EditKey("check.position", key => key.Position += new Vector3(0.01f, 0, 0));
            MoveKey(insertedAt + 0.009f);
            Require(Clip.Keys.Any(k => MathF.Abs(k.At - (insertedAt + 0.009f)) < 0.0001f), "Moving an interior key must preserve it at the new time.");
            DeleteKey();
            Require(Clip.Keys.Count == originalKeys, "Delete key must remove only the selected interior key.");
            Undo();
            Require(Clip.Keys.Count == originalKeys + 1, "Undo must restore a deleted key.");
            SetPhase(0);
            SetView(1);
            _showGizmo = true;
            await Settle(2);
            UpdateGizmo();
            Vector2 gripScreen = ProjectViewmodel(_gizmo.GlobalPosition);
            int dragAxis = Enumerable.Range(0, 3).OrderByDescending(i => ProjectViewmodel(_gizmo.GlobalPosition + GizmoAxes[i] * GizmoArm).DistanceTo(gripScreen)).First();
            Vector2 tipScreen = ProjectViewmodel(_gizmo.GlobalPosition + GizmoAxes[dragAxis] * GizmoArm);
            Vector2 screenDelta = tipScreen - gripScreen;
            Vector3 beforeDrag = _profile.FirstPerson.Position;
            _previewContainer.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = tipScreen });
            Require(_dragAxis == dragAxis, "Clicking the rendered gizmo tip must select its axis.");
            Vector2 dragEnd = tipScreen + screenDelta.Normalized() * (screenDelta.Length() / GizmoArm * 0.02f);
            _previewContainer.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Position = dragEnd, ButtonMask = MouseButtonMask.Left });
            _previewContainer.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = dragEnd });
            Require(MathF.Abs(_profile.FirstPerson.Position[dragAxis] - beforeDrag[dragAxis] - 0.02f) < 0.0002f, "Dragging an axis must move the FPS grip by the projected distance.");
            _testPositionX!.GetLineEdit().GrabFocus();
            _Input(new InputEventKey { Keycode = Key.Z, CtrlPressed = true, Pressed = true });
            Require(_profile.FirstPerson.Position.DistanceTo(beforeDrag) < 0.0002f, "Ctrl+Z while a numeric field has focus must undo the gizmo edit.");
            _showGizmo = false;
            foreach (HeldPresentationProfile original in originals.Values) HeldPresentations.Replace(HeldPresentations.Clone(original));
            _dirty.Clear(); _undo.Clear(); _redo.Clear();

            // Cover every held item and every authored action through both gameplay rendering paths.
            // --editor-check-only can narrow an interactive debugging run; the default is exhaustive.
            string[] only = Option("editor-check-only", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (HeldItemModel item in HeldItems.All)
            {
                if (only.Length > 0 && !only.Contains(item.Id, StringComparer.Ordinal)) continue;
                SelectItem(item);
                foreach (string action in _actionNames.ToArray())
                {
                    SelectAction(action);
                    actions++;
                    float actionAttachmentMax = 0, actionAttachmentPhase = 0;
                    int actionAttachmentWarnings = 0;
                    int actionAttachmentChecks = 0;
                    float actionReachCorrection = 0;
                    float[] phases = Clip.Keys.Select(k => k.At)
                        .Concat(Clip.Keys.Zip(Clip.Keys.Skip(1), (a, b) => (a.At + b.At) * 0.5f))
                        .Append(Clip.Marker >= 0 ? Clip.Marker : 0.35f).Append(0.65f)
                        .Distinct().Order().ToArray();
                    foreach (int camera in new[] { 0, 2 })
                    {
                        SetView(camera);
                        foreach (float phase in phases)
                        {
                            SetPhase(phase);
                            await Settle(2);
                            CheckFinite(camera == 0 ? _viewmodel : _character, $"{item.Id}/{action}/{camera}/{phase}");
                            if (action == "reload")
                            {
                                bool hasMagazine = camera == 0 ? _viewmodel.HasReloadMagazine : _character.HasReloadMagazine;
                                bool carrying = camera == 0 ? _viewmodel.CarryingReloadMagazine : _character.CarryingReloadMagazine;
                                if (item.Id is "pistol" or "rifle")
                                    Require(hasMagazine, $"{item.Id}/{camera}: imported model is missing its moving magazine.");
                                if (hasMagazine)
                                    Require(carrying == (phase >= 0.28f && phase < 0.62f),
                                        $"{item.Id}/{camera}/{phase}: the actual magazine must leave and return to the gun with the support hand.");
                            }
                            if (camera == 0)
                            {
                                foreach (var hand in new[]
                                {
                                    (Error: _viewmodel.PrimaryHandAttachmentError, Attach: _viewmodel.PrimaryHandShouldAttach, Name: "primary"),
                                    (Error: _viewmodel.SecondaryHandAttachmentError, Attach: _viewmodel.SecondaryHandShouldAttach, Name: "secondary"),
                                })
                                {
                                    if (!hand.Attach) continue;
                                    actionAttachmentChecks++;
                                    if (hand.Error > maxAttachmentError) { maxAttachmentError = hand.Error; maxAttachmentContext = $"{item.Id}/{action}/{phase}/{hand.Name}"; }
                                    if (hand.Error > 0.025f) attachmentWarnings++;
                                    if (hand.Error > actionAttachmentMax) { actionAttachmentMax = hand.Error; actionAttachmentPhase = phase; }
                                    if (hand.Error > 0.025f) actionAttachmentWarnings++;
                                }
                                float correction = _viewmodel.AuthoredReachCorrection;
                                actionReachCorrection = Math.Max(actionReachCorrection, correction);
                                if (correction > maxReachCorrection) { maxReachCorrection = correction; maxReachContext = $"{item.Id}/{action}/{phase}"; }
                            }
                            Require(camera == 0 ? _viewmodel.CurrentPresentation?.Item == item.Id : _character.CurrentPresentation?.Item == item.Id,
                                "The preview renderer must use the selected item's profile.");
                            samples++;
                        }
                    }
                    GD.Print($"[held-attachment-action] {item.Id}/{action} max={actionAttachmentMax:0.0000} m phase={actionAttachmentPhase:0.000}; over25mm={actionAttachmentWarnings}/{actionAttachmentChecks}; authoredReachCorrection={actionReachCorrection:0.0000} m");
                }
            }
            GD.Print($"[held-editor-attachment] max={maxAttachmentError:0.0000} m at {maxAttachmentContext}; {attachmentWarnings} connected FPS samples exceed 0.025 m.");
            GD.Print($"[held-editor-reach] largest authored-pose correction={maxReachCorrection:0.0000} m at {maxReachContext}; corrections above 0.005 m are visible editor warnings.");
            Require(attachmentWarnings == 0, $"A holding hand detached by more than 0.025 m in {attachmentWarnings} samples; worst {maxAttachmentError:0.0000} m at {maxAttachmentContext}.");
            GD.Print($"[held-editor-check] PASS: numeric UI edit, live preview, undo, redo, key add/update/move/delete/undo, actual gizmo drag, Ctrl+Z with field focus, atomic temporary save/reload; {actions} actions, {samples} FPS/TPS samples with finite transforms and attached holding hands.");
            _statusText = $"Editor check passed · {actions} actions · {samples} FPS/TPS samples";
        }
        catch (Exception error)
        {
            result = 1;
            GD.PushError("[held-editor-check] FAIL: " + error);
            _statusText = "Editor check failed: " + error.Message;
        }
        finally
        {
            foreach (HeldPresentationProfile original in originals.Values) HeldPresentations.Replace(HeldPresentations.Clone(original));
            _dirty.Clear();
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        if (_options.TryGetValue("shot", out string? path))
        {
            SelectItem(HeldItems.Find(Option("item", "metal-hatchet")) ?? HeldItems.All[0]);
            SelectAction(Option("action", "idle"));
            SetPhase((float)Number("phase", 0));
            SetView(Option("camera", "fps") == "tps" ? 2 : 0);
            await Settle(3);
            await WriteScreenshot(path);
        }
        GetTree().Quit(result);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void CheckFinite(Node node, string context)
    {
        if (node is Node3D spatial)
        {
            Transform3D t = spatial.Transform;
            Require(Finite(t.Origin) && Finite(t.Basis.X) && Finite(t.Basis.Y) && Finite(t.Basis.Z), "Nonfinite transform: " + context + "/" + node.Name);
        }
        if (node is Skeleton3D skeleton)
        {
            for (int i = 0; i < skeleton.GetBoneCount(); i++)
            {
                Transform3D pose = skeleton.GetBoneGlobalPose(i);
                Require(Finite(pose.Origin) && Finite(pose.Basis.X) && Finite(pose.Basis.Y) && Finite(pose.Basis.Z), "Nonfinite bone: " + context + "/" + skeleton.GetBoneName(i));
            }
        }
        foreach (Node child in node.GetChildren()) CheckFinite(child, context);
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private async Task Settle(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>One row per action and view; columns rest, anticipation, event, recovery, end.</summary>
    private async void CaptureSheet(string path)
    {
        try
        {
            SetPlaying(false);
            string[] actions = _options.ContainsKey("action") ? new[] { _action } : _actionNames.ToArray();
            int[] views = _options.ContainsKey("camera") ? new[] { _view } : new[] { 0, 2 };
            const int columns = 5, width = 400, height = 300;
            Image sheet = Image.CreateEmpty(columns * width, actions.Length * views.Length * height, false, Image.Format.Rgba8);
            sheet.Fill(new Color(0.025f, 0.035f, 0.05f));
            _captureCaption.Visible = true;
            _captureCaption.AddThemeFontSizeOverride("font_size", HollowUniverse.Common.Ui.Px(24));
            // A fixed 4:3 render size provides comparable framing for every tile, independent of the editor panels.
            _previewContainer.Stretch = false;
            _preview.Size = new Vector2I(width * 2, height * 2);
            int row = 0;
            foreach (string action in actions)
            {
                SelectAction(action);
                foreach (int camera in views)
                {
                    SetView(camera);
                    float[] phases = { 0, 0.20f, Clip.Marker >= 0 ? Clip.Marker : 0.40f, 0.65f, 1 };
                    for (int col = 0; col < phases.Length; col++)
                    {
                        SetPhase(phases[col]);
                        _captureCaption.Text = $"{_held.Name} · {(camera == 0 ? "FPS" : camera == 1 ? "arms orbit" : "TPS")}\n{action} · {phases[col] * 100:0.#}%" + (col == 2 && Clip.Marker >= 0 ? " · EVENT" : "");
                        await Settle(3);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        Image frame = _preview.GetTexture().GetImage();
                        frame.Convert(Image.Format.Rgba8);
                        frame.Resize(width, height, Image.Interpolation.Lanczos);
                        sheet.BlitRect(frame, new Rect2I(0, 0, width, height), new Vector2I(col * width, row * height));
                    }
                    row++;
                }
            }
            EnsureOutputDirectory(path);
            Error error = sheet.SavePng(path);
            Require(error == Error.Ok, "Could not save contact sheet: " + error);
            GD.Print($"[held-editor-sheet] Saved {path}: {actions.Length} actions, {views.Length} views, {columns} phases.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("[held-editor-sheet] " + error); GetTree().Quit(1); }
    }

    private async void Capture(string path)
    {
        try { await WriteScreenshot(path); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("[held-editor-shot] " + error); GetTree().Quit(1); }
    }

    /// <summary>Deterministic authoring samples rendered by the actual gameplay rigs, ready for an external encoder.</summary>
    private async void CaptureAnimation(string directory)
    {
        const int fps = 24, maximumFrames = 240, width = 1280, height = 720;
        const double maximumSeconds = 10;
        try
        {
            SetPlaying(false);
            string cycle = Option("editor-cycle", "");
            Require(cycle.Length == 0 || cycle == "bow", "The supported animation cycle is --editor-cycle bow.");
            if (_options.TryGetValue("action", out string? requestedAction))
                Require(_profile.Actions.ContainsKey(requestedAction), $"The selected item has no '{requestedAction}' action.");
            var segments = new List<(string Action, double Seconds)>();
            if (cycle == "bow")
            {
                Require(_content.Item(_held.Id)?.Bow != null, "The bow cycle requires a bow item, for example --item bow.");
                foreach (string action in new[] { "idle", "draw", "release" })
                    Require(_profile.Actions.ContainsKey(action), $"The bow cycle requires the '{action}' action.");
                double drawSeconds = HeldActionTiming.Duration(_content.Item(_held.Id), "draw", _profile.Actions["draw"].Duration);
                segments.Add(("idle", 0.2));
                segments.Add(("draw", drawSeconds));
                segments.Add(("release", 0.65));
                segments.Add(("idle", 0.25));
            }
            else
            {
                double seconds = _gameTiming ? HeldActionTiming.Duration(_content.Item(_held.Id), _action, Clip.Duration) : Clip.Duration;
                segments.Add((_action, seconds));
            }
            double requestedSeconds = segments.Sum(s => s.Seconds);
            Require(double.IsFinite(requestedSeconds) && requestedSeconds > 0, "Animation duration must be finite and positive.");
            double secondsToCapture = Math.Min(maximumSeconds, requestedSeconds);
            int frameCount = Math.Clamp((int)Math.Ceiling(secondsToCapture * fps), 2, maximumFrames);
            string output = Path.GetFullPath(directory);
            Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Animation output directory must be empty so old frames cannot enter the sequence: " + output);
            Directory.CreateDirectory(output);
            _previewContainer.Stretch = false;
            _preview.Size = new Vector2I(width, height);
            _captureCaption.Visible = true;
            _captureCaption.AddThemeFontSizeOverride("font_size", HollowUniverse.Common.Ui.Px(24));
            SetView(_view);
            string cameraName = _view == 0 ? "fps" : _view == 1 ? "external" : "tps";
            int segmentIndex = 0;
            double segmentStart = 0;
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                // Include exact first/last poses; rounding differs from nominal playback by less than one frame.
                double time = secondsToCapture * frameIndex / (frameCount - 1);
                while (segmentIndex < segments.Count - 1 && time >= segmentStart + segments[segmentIndex].Seconds)
                {
                    segmentStart += segments[segmentIndex].Seconds;
                    segmentIndex++;
                }
                (string action, double seconds) = segments[segmentIndex];
                if (_action != action) SelectAction(action);
                float phase = (float)Math.Clamp((time - segmentStart) / seconds, 0, 1);
                // Short idle pauses retain the ordinary idle speed instead of squeezing an entire breath into 0.2s.
                if (cycle == "bow" && action == "idle") phase = (float)((time - segmentStart) % Clip.Duration / Clip.Duration);
                float marker = _gameTiming ? GameplayMarker() : -1;
                SetPhase(marker >= 0 ? Clip.AlignMarker(phase, marker) : phase);
                _captureCaption.Text = $"{_held.Name} · {cameraName.ToUpperInvariant()}\n{action} · {time:0.000} s · {_phase * 100:0.0}%";
                await Settle(2);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                CheckFinite(_view == 2 ? _character : _viewmodel, $"animation/{_held.Id}/{action}/{_phase}");
                using Image image = _preview.GetTexture().GetImage();
                Error saved = image.SavePng(Path.Combine(output, $"frame-{frameIndex:0000}.png"));
                Require(saved == Error.Ok, "Could not save animation frame " + frameIndex + ": " + saved);
            }
            string manifest = JsonSerializer.Serialize(new
            {
                version = 1, item = _held.Id, camera = cameraName, cycle,
                fps, frameCount, width, height, framePattern = "frame-%04d.png",
                sampledSeconds = secondsToCapture, playbackSeconds = frameCount / (double)fps,
                requestedSeconds, truncated = requestedSeconds > secondsToCapture,
                gameplayTiming = _gameTiming, aiming = _aiming,
                segments = segments.Select(s => new { action = s.Action, seconds = s.Seconds }),
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(output, "manifest.json"), manifest + System.Environment.NewLine);
            GD.Print($"[held-editor-animation] Saved {frameCount} frames at {fps} fps to {output}; {secondsToCapture:0.000}s sampled, camera={cameraName}, cycle={cycle}.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("[held-editor-animation] " + error); GetTree().Quit(1); }
    }

    private async Task WriteScreenshot(string path)
    {
        await Settle(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        EnsureOutputDirectory(path);
        Error error = GetViewport().GetTexture().GetImage().SavePng(path);
        Require(error == Error.Ok, "Could not save screenshot: " + error);
        GD.Print("[held-editor-shot] Saved " + path);
    }

    private static void EnsureOutputDirectory(string path)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
    }
}
