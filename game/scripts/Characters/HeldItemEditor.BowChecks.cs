using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Characters;

public partial class HeldItemEditor
{
    private sealed record BowCheckSample(string Body, string Item, string State, float Phase, int Width, int Height,
        float CameraFov, float ViewmodelFov, bool ArrowVisible, float AxisDegrees, float? CenterError,
        float[]? ScreenAt10Metres, float[] ArrowOrigin, float[] ArrowAxis, float PalmGap,
        float PrimaryAttachment, float SecondaryAttachment, float NockDepth, BowHandCheck LeftHand, BowHandCheck RightHand);
    private sealed record BowHandCheck(float[] ShoulderCamera, float[] ElbowCamera, float[] WristCamera,
        float[]? ProjectedBoneBounds, float MinimumBoneDepth, int BonePoints, int NearPlaneOrBehindPoints);

    /// <summary>Measure the real arrow mesh and its shader-equivalent projection; never edit held profiles.</summary>
    private async void RunBowChecks()
    {
        const float axisLimit = 1f, screenLimit = 0.015f, gripLimit = 0.025f, minimumNockDepth = 0.18f;
        bool reportOnly = _options.ContainsKey("bow-check-report-only"), outputReady = false;
        string output = Option("bow-check-out", Path.Combine(
            ProjectSettings.GlobalizePath("user://"), "bow-check-" + Guid.NewGuid().ToString("N")));
        var samples = new List<BowCheckSample>();
        var failures = new List<string>();
        var alignmentFailures = new List<string>();
        var screenshots = new List<string>();
        int shoulderOffsetChecks = 0;
        float originalFov = CharacterMaterials.ViewmodelFov;
        void SaveReport() => File.WriteAllText(Path.Combine(output, "bow-check.json"), JsonSerializer.Serialize(new
        {
            reportOnly, axisLimitDegrees = axisLimit, normalizedScreenRadiusLimit = screenLimit,
            gripLimitMetres = gripLimit, projectionDepthMetres = 10, alignmentBeginsAtDrawPhase = 0.25,
            minimumFullDrawNockDepthMetres = minimumNockDepth,
            handBounds = "Normalized [minX,minY,maxX,maxY] of wrist and descendant finger joints in front of the near plane; a bone proxy, not skinned surface coverage or visual approval.",
            projection = "Actual Arrow +X ray; ProjectViewmodel matches the material's independent vertical FOV.",
            sourceFingerprint = DiskFingerprint(), shoulderOffsetChecks, failures, alignmentFailures, screenshots, samples,
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        BowCheckSample Record(string state, float phase)
        {
            BowCheckSample sample = MeasureBow(state, phase);
            samples.Add(sample);
            string context = $"{sample.Body}/{sample.Item}/{state}/{phase:0.000}/{sample.Width}x{sample.Height}/camera{sample.CameraFov}/item{sample.ViewmodelFov}";
            if (sample.PalmGap > gripLimit || sample.PrimaryAttachment > gripLimit ||
                _viewmodel.SecondaryHandShouldAttach && sample.SecondaryAttachment > gripLimit)
                failures.Add(context + $": grip error; palm={sample.PalmGap:0.0000}, primary={sample.PrimaryAttachment:0.0000}, secondary={sample.SecondaryAttachment:0.0000} m.");
            if (!sample.ArrowVisible) failures.Add(context + ": drawn arrow is hidden.");
            if (phase >= 0.999f && sample.NockDepth < minimumNockDepth)
                failures.Add(context + $": full-draw nock depth {sample.NockDepth:0.0000} m is below {minimumNockDepth:0.00} m.");
            if (phase >= 0.25f && (sample.AxisDegrees > axisLimit || !sample.CenterError.HasValue || sample.CenterError > screenLimit))
                alignmentFailures.Add(context + $": arrow axis={sample.AxisDegrees:0.000} degrees, projected center error={sample.CenterError?.ToString("0.00000") ?? "no forward intersection"}.");
            return sample;
        }
        try
        {
            output = Path.GetFullPath(output);
            Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Bow check output must be a new or empty directory: " + output);
            Directory.CreateDirectory(output); outputReady = true;
            // The workshop normally resets the camera FOV every frame. Freeze only that controller;
            // its child gameplay renderer still processes, and SetPhase samples the actual selected pose.
            SetProcess(false); SetPlaying(false); _aiming = true; _contactPreview = false; _showTarget = false;
            SetView(0); _previewContainer.Stretch = false; _captureCaption.Visible = true;
            var cross = new ColorRect { Color = new Color(0.3f, 1f, 0.8f), Size = new Vector2(Ui.Px(12), Ui.Px(2)), MouseFilter = Control.MouseFilterEnum.Ignore };
            var vertical = new ColorRect { Color = cross.Color, Size = new Vector2(Ui.Px(2), Ui.Px(12)), MouseFilter = Control.MouseFilterEnum.Ignore };
            _captureCaption.GetParent().AddChild(cross); _captureCaption.GetParent().AddChild(vertical);
            HeldItemModel[] bows = HeldItems.All.Where(item => item.LeftHanded && _content.Item(item.Id)?.Bow != null).ToArray();
            Require(bows.Length > 0, "No left-handed bow capability was found.");
            foreach (BodyType body in new[] { BodyType.Male, BodyType.Female })
            {
                _body = body; BuildCharacters();
                foreach (HeldItemModel bow in bows)
                {
                    SelectItem(bow); SelectAction("draw");
                    Require(_action == "draw", bow.Id + ": draw presentation is missing.");
                    CheckBowShoulderOffsetData(); shoulderOffsetChecks++;
                    foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(960, 720), new Vector2I(1680, 720) })
                    foreach (float cameraFov in new[] { 54f, 90f })
                    foreach (float itemFov in new[] { 54f, 70f, 90f })
                    {
                        _preview.Size = size;
                        cross.Position = (Vector2)size * 0.5f - cross.Size * 0.5f;
                        vertical.Position = (Vector2)size * 0.5f - vertical.Size * 0.5f;
                        UpdateCamera(); _camera.Fov = cameraFov; CharacterMaterials.SetViewmodelFov(itemFov);
                        foreach (float phase in new[] { 0f, 0.125f, 0.25f, 0.5f, 0.75f, 1f })
                        {
                            SetPhase(phase); await Settle(1);
                            BowCheckSample sample = Record("draw-preview", phase);
                            if (bow == bows[0] && size.X == 1280 && cameraFov == 54 && itemFov == 70 && phase == 1)
                            {
                                _captureCaption.Text = $"{body} · {bow.Name} · full draw · camera {cameraFov}° / item {itemFov}°\n" +
                                    $"Arrow axis {sample.AxisDegrees:0.00}° · center error {sample.CenterError:0.0000} · cyan = crosshair";
                                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                                string file = body.ToString().ToLowerInvariant() + "-full-draw.png";
                                using Image frame = _preview.GetTexture().GetImage();
                                Require(frame.SavePng(Path.Combine(output, file)) == Error.Ok, "Could not write bow check screenshot.");
                                screenshots.Add(file);
                            }
                        }
                    }
                    // Repeat the live draw after admiration, then release while aim stays held and redraw.
                    _viewmodel.ClearPreview(); _viewmodel.PlayAction("admire"); _viewmodel._Process(0.2);
                    _viewmodel.CancelActions(); _viewmodel.Apply(new ViewmodelFrame { Grounded = true, Aiming = true });
                    _viewmodel._Process(bow.DrawSeconds + 0.1); Record("draw-after-admire", 1);
                    _viewmodel.PlayAction("release"); _viewmodel._Process(0.02);
                    Require(_viewmodel.ActiveAction == "release", bow.Id + ": live release did not start.");
                    for (int tick = 0; tick < 180; tick++) _viewmodel._Process(1d / 60d);
                    Require(_viewmodel.ActiveAction == "draw", bow.Id + ": continued aiming must return to draw after release.");
                    Record("redraw-after-release", 1);
                }
            }
            SaveReport();
            Require(failures.Count == 0, string.Join("\n", failures.Take(12)));
            Require(reportOnly || alignmentFailures.Count == 0, string.Join("\n", alignmentFailures.Take(12)));
            GD.Print($"[bow-check] {(reportOnly ? "REPORT" : "PASS")}: {samples.Count} samples, {alignmentFailures.Count} alignment violations; {output}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            failures.Add(error.Message);
            if (outputReady) SaveReport();
            GD.PushError("[bow-check] FAIL: " + error); GetTree().Quit(1);
        }
        finally { CharacterMaterials.SetViewmodelFov(originalFov); }
    }

    private BowCheckSample MeasureBow(string state, float phase)
    {
        CheckFinite(_viewmodel, _held.Id + "/bow/" + state);
        Node3D arrow = _viewmodel.GetNode<Node3D>(_held.Id).GetNodeOrNull<Node3D>("Arrow")
            ?? throw new InvalidOperationException(_held.Id + ": the actual held Arrow node is missing.");
        Vector3 origin = arrow.GlobalPosition, axis = arrow.GlobalBasis.X.Normalized();
        Transform3D cameraInverse = _camera.GlobalTransform.AffineInverse();
        Vector3 localOrigin = cameraInverse * origin, localAxis = cameraInverse.Basis * axis;
        float degrees = Mathf.RadToDeg(MathF.Acos(Math.Clamp(axis.Dot(-_camera.GlobalBasis.Z.Normalized()), -1, 1)));
        float? centerError = null; float[]? screenPoint = null;
        if (localAxis.Z < -0.00001f)
        {
            float distance = (-10 - localOrigin.Z) / localAxis.Z;
            if (distance > 0)
            {
                Vector2 normalized = ProjectViewmodel(origin + axis * distance) / (Vector2)_preview.Size;
                centerError = normalized.DistanceTo(new Vector2(0.5f, 0.5f));
                screenPoint = new[] { normalized.X, normalized.Y };
            }
        }
        float palm = _viewmodel.PrimaryPalmContactError, primary = _viewmodel.PrimaryHandAttachmentError, secondary = _viewmodel.SecondaryHandAttachmentError;
        Require(Finite(axis) && float.IsFinite(degrees) && (!centerError.HasValue || float.IsFinite(centerError.Value)) &&
            float.IsFinite(palm) && float.IsFinite(primary) && float.IsFinite(secondary), "Nonfinite bow projection or grip measurement.");
        return new BowCheckSample(_body.ToString(), _held.Id, state, phase, _preview.Size.X, _preview.Size.Y,
            _camera.Fov, CharacterMaterials.ViewmodelFov, arrow.IsVisibleInTree(), degrees, centerError, screenPoint,
            new[] { localOrigin.X, localOrigin.Y, localOrigin.Z }, new[] { localAxis.X, localAxis.Y, localAxis.Z }, palm, primary, secondary,
            -localOrigin.Z, MeasureBowHand(false), MeasureBowHand(true));
    }

    private BowHandCheck MeasureBowHand(bool right)
    {
        Skeleton3D skeleton = FindCheckSkeleton(_viewmodel);
        string side = right ? "_r" : "_l";
        int shoulder = skeleton.FindBone("upperarm" + side), elbow = skeleton.FindBone("lowerarm" + side), hand = skeleton.FindBone("hand" + side);
        Require(shoulder >= 0 && elbow >= 0 && hand >= 0, "Missing bow arm diagnostic bones: " + side);
        Transform3D cameraInverse = _camera.GlobalTransform.AffineInverse();
        Vector3 World(int bone) => skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone).Origin;
        float[] Camera(int bone) { Vector3 point = cameraInverse * World(bone); return new[] { point.X, point.Y, point.Z }; }
        Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity), maximum = new(float.NegativeInfinity, float.NegativeInfinity);
        float minimumDepth = float.PositiveInfinity;
        int points = 0, clipped = 0;
        for (int bone = 0; bone < skeleton.GetBoneCount(); bone++)
        {
            int ancestor = bone;
            while (ancestor >= 0 && ancestor != hand) ancestor = skeleton.GetBoneParent(ancestor);
            if (ancestor != hand) continue;
            Vector3 world = World(bone), local = cameraInverse * world;
            Require(Finite(local), "Nonfinite bow hand point: " + skeleton.GetBoneName(bone));
            minimumDepth = Math.Min(minimumDepth, -local.Z); points++;
            if (-local.Z <= _camera.Near) { clipped++; continue; }
            Vector2 projected = ProjectViewmodel(world) / (Vector2)_preview.Size;
            Require(float.IsFinite(projected.X) && float.IsFinite(projected.Y), "Nonfinite projected bow hand point.");
            minimum = new Vector2(Math.Min(minimum.X, projected.X), Math.Min(minimum.Y, projected.Y));
            maximum = new Vector2(Math.Max(maximum.X, projected.X), Math.Max(maximum.Y, projected.Y));
        }
        Require(points > 0 && float.IsFinite(minimumDepth), "No finite hand points for bow framing diagnostic.");
        float[]? bounds = points > clipped ? new[] { minimum.X, minimum.Y, maximum.X, maximum.Y } : null;
        return new BowHandCheck(Camera(shoulder), Camera(elbow), Camera(hand), bounds, minimumDepth, points, clipped);
    }

    private void CheckBowShoulderOffsetData()
    {
        HeldPresentationProfile source = HeldPresentations.Get(_held.Id);
        HeldPresentationProfile copy = HeldPresentations.Clone(source);
        Require(copy.LeftHand.FirstPersonShoulderOffset == source.LeftHand.FirstPersonShoulderOffset &&
            copy.RightHand.FirstPersonShoulderOffset == source.RightHand.FirstPersonShoulderOffset,
            _held.Id + ": cloning must retain both authored first-person shoulder offsets.");
        Require(HeldPresentations.Validate(copy).Count == 0, _held.Id + ": cloned shoulder profile must remain valid.");
        foreach (bool right in new[] { false, true })
        {
            HeldPresentationProfile invalid = HeldPresentations.Clone(source);
            HeldPresentationHand hand = right ? invalid.RightHand : invalid.LeftHand;
            hand.FirstPersonShoulderOffset = new Vector3(float.NaN, 0, 0);
            string field = (right ? "rightHand" : "leftHand") + ".firstPersonShoulderOffset";
            Require(HeldPresentations.Validate(invalid).Any(error => error.Contains(field, StringComparison.Ordinal)),
                _held.Id + ": nonfinite " + field + " must be rejected before entering the live catalog.");
        }
        Require(source.LeftHand.FirstPersonShoulderOffset == copy.LeftHand.FirstPersonShoulderOffset &&
            source.RightHand.FirstPersonShoulderOffset == copy.RightHand.FirstPersonShoulderOffset,
            _held.Id + ": invalid-clone validation must not mutate the live shoulder profile.");
    }
}
